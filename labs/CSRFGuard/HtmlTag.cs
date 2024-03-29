using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Org.Owasp.CsrfGuard
{
    public class HtmlTag
    {
        // collection holding all attributes
        private ListDictionary _attrList = new ListDictionary();
        private String _fullTagText;
        private String _tagName; // e.g. <img has a tag name of "img"
        private bool _selfClosing; // whether this is a self-closing tag or not
        private bool _isStartTag; // is this a start or an end tag?

        // constructor
        public HtmlTag(String fullTagText)
        {
            _fullTagText = fullTagText;
            if (!IsEntityTag())
            {
                NormalizeHtmlSpacingForParsing();
                Regex tagNameRegex = new Regex("<([^ ]+)");
                Match m = tagNameRegex.Match(_fullTagText);
                if (m.Success)
                {
                    _tagName = m.Groups[1].Captures[0].Value.ToLower(CultureInfo.CurrentCulture);
                }

                if (TagHasAttributes())
                {
                    PopulateTagAttributes();
                    UpdateTagString();
                }
            }
        }

        // tokenizes the Html tag string and populates the attributes Hash table

        #region methods

        protected void PopulateTagAttributes()
        {
            // 
            String[] tokens = Split(_fullTagText, " ", "\"", true);

            foreach (String token in tokens)
            {
                if (token.IndexOf('=') > 0)
                {
                    String[] attr = Split(token.ToString(), "=", "\"", true);

                    _attrList.Add(attr[0].ToLower(CultureInfo.CurrentCulture), attr[1]);
                }
            }
        }

        // normalizes whitespace in the Html
        protected void NormalizeHtmlSpacingForParsing()
        {
            // <a    href = "/my/url/that has spaces.aspx" id = 1>
            // normalize any closing tag endings to be offset by a space to make splitting the attributes easier
            // also, set tag information based on the matches
            Regex startTagRegex = new Regex("</", RegexOptions.Compiled);
            if (!startTagRegex.Match(_fullTagText).Success)
            {
                _isStartTag = true;
            }

            Regex endTagRegex = new Regex("/>", RegexOptions.Compiled);
            if (endTagRegex.Match(_fullTagText).Success)
            {
                // capture whether this tag is self-closing or not
                _selfClosing = true;
                _fullTagText = endTagRegex.Replace(_fullTagText, " />");
            }
            else
            {
                // not self-closing
                Regex endTagRegex2 = new Regex("([^/])>", RegexOptions.Compiled);

                if (_fullTagText.IndexOf(' ') > -1)
                {
                    // tag contains whitespace so it cannot be a tag like <html>.  Separate the end bracket from the rest of the tak for the tokenizing process later.
                    _fullTagText = endTagRegex2.Replace(_fullTagText, "$1 >");
                }
                else
                {
                    // avoid injecting whitespace in a nice simple tag like <html> or <head>
                    _fullTagText = endTagRegex2.Replace(_fullTagText, "$1>");
                }
            }

            // replace consecutive spaces with a single space and remove spaces around equals while ignoring them in quoted strings
            _fullTagText = CompressWhitespace(_fullTagText, "\"");
        }

        // rewrite the tag string with the latest attributes
        protected void UpdateTagString()
        {
            StringBuilder newTag = new StringBuilder();

            // add start of tag and double-quote
            newTag.Append("<");
            newTag.Append(Name);

            // add all of the attributes in the original order
            foreach (DictionaryEntry item in _attrList)
            {
                newTag.Append(" ");
                newTag.Append(item.Key + "=" + item.Value);
            }

            // add the closing tag and double-quote
            if (IsSelfClosing)
            {
                newTag.Append(" />");
            }
            else
            {
                newTag.Append(">");
            }
            _fullTagText = newTag.ToString();
        }

        #endregion

        //-------
        // getters and setters
        //-------

        public int AttrCount
        {
            get { return _attrList.Count; }
        }

        public String TagString
        {
            get { return _fullTagText; }
        }

        public bool IsSelfClosing
        {
            get { return _selfClosing; }
        }

        public bool IsStartTag
        {
            get { return _isStartTag; }
        }

        public String Name
        {
            get { return _tagName; }
        }

        // Oh why make properties not easily able to deal with data structures in a safe way.  No way am I returning the whole
        // hashtable to the caller to screw up.
        public String GetAttributeValue(String attrName)
        {
            // avoid null reference exception with ToString()
            if (_attrList.Contains(attrName))
            {
                return _attrList[attrName].ToString();
            }
            else
            {
                return null;
            }
        }

        public void SetAttributeValue(String attrName, String value)
        {
            if (_attrList.Contains(attrName))
            {
                _attrList[attrName] = value;
            }
            else
            {
                _attrList.Add(attrName, value);
            }
            // update the tag string since we changed the attribute value
            UpdateTagString();
        }

        //-------
        // helper methods
        //-------

        // determines whether a given html tag string set on the object contains html attributes (e.g. href="/some/where.html")
        protected bool TagHasAttributes()
        {
            bool isAttrFound = false;
            int eqIdx;
            // first, look for an equals sign as possible evidence of an html attribute
            if (((eqIdx = _fullTagText.IndexOf('=')) > 0))
            {
                // now, rule out an equals inside some other quoted string so we know it's part of the attribute definition markup
                int quoteIdx;
                quoteIdx = _fullTagText.IndexOf('"');
                int quoteIdx2;
                quoteIdx2 = _fullTagText.IndexOf('\'');
                // if we found a double quote and it is to the right of the equals, OK
                if ((quoteIdx > 0) && (eqIdx < quoteIdx))
                {
                    isAttrFound = true;
                }
                // and if we found a single quote and it is to the right of the equals, OK
                if ((quoteIdx2 > 0) && (eqIdx < quoteIdx2))
                {
                    isAttrFound = true;
                }
            }
            return isAttrFound;
        }

        /// <summary>
        /// Tags beginning with &lt;! are special and probably don't have attributes, but we don't want to lose their innards.
        /// </summary>
        protected bool IsEntityTag()
        {
            // look for a ! as the second char of the entity, as in <!DOCTYPE
            if (_fullTagText[1] == '!')
            {
                return true;
            }
            return false;
        }

        // String split that supports text qualifiers so we can split text on a delimiter but ignore delimiters inside quotes
        // http://www.codeproject.com/useritems/TextQualifyingSplit.asp?df=100&forumid=336054&exp=0&select=1798414
        public string[] Split(string str, string delimiter, string qualifier, bool ignoreCase)
        {
            bool _QualifierState = false;
            int _StartIndex = 0;
            ArrayList _Values = new ArrayList();

            for (int _CharIndex = 0; _CharIndex < str.Length - 1; _CharIndex++)
            {
                if ((qualifier != null)
                    & (string.Compare(str.Substring(_CharIndex, qualifier.Length), qualifier, ignoreCase, CultureInfo.CurrentCulture) == 0))
                {
                    _QualifierState = !(_QualifierState);
                }
                else if (!(_QualifierState) & (delimiter != null)
                         & (string.Compare(str.Substring(_CharIndex, delimiter.Length), delimiter, ignoreCase, CultureInfo.CurrentCulture) == 0))
                {
                    _Values.Add(str.Substring(_StartIndex, _CharIndex - _StartIndex));
                    _StartIndex = _CharIndex + 1;
                }
            }

            if (_StartIndex < str.Length)
                _Values.Add(str.Substring(_StartIndex, str.Length - _StartIndex));

            string[] _returnValues = new string[_Values.Count];
            _Values.CopyTo(_returnValues);
            return _returnValues;
        }

        /// <summary>
        /// Replaces two or more consecutive spaces with a single space, but only outside of quoted strings!  And, it will not leave whitespace around = signs outside of a quoted string
        /// </summary>
        /// <param name="str">String to compress whitespace</param>
        /// <param name="qualifier">Quoted string qualifier.  " by default</param>
        public String CompressWhitespace(string str, string qualifier)
        {
            const int MAX_CONSEC_SPACES = 2;
            bool insideQuotedString = false;
            bool gotEqualsOutsideQuotedString = false;
            int consecWsCount = 0; // how many consecutive spaces we've found.
            StringBuilder sb = new StringBuilder();
            StringBuilder space = new StringBuilder();

            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == qualifier[0])
                {
                    // state = inside quoted string or transitioning out of one.  Append the qualifier and continue on.
                    insideQuotedString = !(insideQuotedString);
                    // if inside a quoted string, then we don't care about the equals state anymore so reset it
                    gotEqualsOutsideQuotedString = false;
                    sb.Append(str[i]);
                }
                else if (str[i] == ' ' && !insideQuotedString)
                {
                    // outside of quoted string and we found a space.  
                    // Compress whitespace by only keeping whitespace < MAX_CONSEC_SPACES chars.
                    // save it to a buffer that will be possibly appended later.
                    if (++consecWsCount < MAX_CONSEC_SPACES)
                    {
                        space.Append(str[i]);
                            // append to whitespace buffer.  This may/may not be appended later depending on if the space is around an equals
                    }
                }
                else if (str[i] == '=' && !insideQuotedString)
                {
                    gotEqualsOutsideQuotedString = true;
                    if (space.Length > 0)
                    {
                        space.Remove(0, space.Length); // discard queued spaces
                    }
                    sb.Append(str[i]);
                }
                else
                {
                    // conditionally append queued space.  Does not print any spaces around = signs outside quoted strings!
                    if (!gotEqualsOutsideQuotedString && space.Length > 0)
                    {
                        sb.Append(space.ToString());
                        space.Remove(0, space.Length); // empty the buffer
                    }
                    gotEqualsOutsideQuotedString = false;

                    // not a whitespace so just append.
                    // reset whitespace count when you hit a non-whitespace char.
                    if (consecWsCount > 0)
                    {
                        consecWsCount = 0;
                    }
                    sb.Append(str[i]);
                }
            }
            return sb.ToString();
        }
    }
}