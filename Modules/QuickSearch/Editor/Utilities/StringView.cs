// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEditor.Search
{
    /// <summary>
    /// Structure that holds a view on a string, with a specified range of [startIndex, endIndex[.
    /// </summary>
    readonly struct StringView : IStringView
    {
        readonly string m_BaseString;
        readonly int m_StartIndex;
        readonly int m_EndIndex;

        public static readonly StringView Null = default;
        public static readonly StringView Empty = new StringView(string.Empty);

        public bool valid => m_BaseString != null;
        public int startIndex => m_StartIndex;
        public int endIndex => m_EndIndex;
        public string baseString => m_BaseString;
        public int Length => m_EndIndex - m_StartIndex;

        public char this[int index] => m_BaseString[m_StartIndex + index];

        public StringView(string baseString)
        {
            m_BaseString = baseString;
            m_StartIndex = 0;
            m_EndIndex = baseString?.Length ?? 0;
        }

        public StringView(string baseString, int startIndex)
            : this(baseString, startIndex, string.IsNullOrEmpty(baseString) ? startIndex : baseString.Length - startIndex)
        {
        }

        public StringView(string baseString, int startIndex, int endIndex)
        {
            if (startIndex < 0 || (!string.IsNullOrEmpty(baseString) && startIndex > baseString.Length) || (string.IsNullOrEmpty(baseString) && startIndex != 0))
                throw new ArgumentException("Index out of string range", nameof(startIndex));
            if (endIndex < 0 || (!string.IsNullOrEmpty(baseString) && endIndex > baseString.Length) || (string.IsNullOrEmpty(baseString) && endIndex != 0))
                throw new ArgumentException("Index out of string range", nameof(endIndex));
            m_BaseString = baseString;
            m_StartIndex = startIndex;
            m_EndIndex = endIndex;
        }

        public override bool Equals(object other)
        {
            if (other is string o)
                return Equals(o);
            if (other is StringView v)
                return Equals(v);
            if (other is IStringView sv)
                return Equals(sv);
            return false;
        }

        public bool Equals(IStringView other, StringComparison comparisonOptions = StringComparison.OrdinalIgnoreCase)
        {
            if (other is StringView sv)
                return Equals(sv, comparisonOptions);

            if (other?.Length != Length)
                return false;

            for (var i = 0; i < Length; ++i)
            {
                if (!Compare(this[i], other[i], comparisonOptions))
                    return false;
            }

            return true;
        }

        public bool Equals(string other, StringComparison comparisonOptions = StringComparison.OrdinalIgnoreCase)
        {
            if (other?.Length != Length)
                return false;

            return string.Compare(m_BaseString, m_StartIndex, other, 0, Length, comparisonOptions) == 0;
        }

        public bool Equals(StringView other, StringComparison comparisonOptions = StringComparison.OrdinalIgnoreCase)
        {
            if (other.Length != Length)
                return false;

            return string.Compare(m_BaseString, m_StartIndex, other.m_BaseString, other.m_StartIndex, Length, comparisonOptions) == 0;
        }

        public StringView Substring(int start)
        {
            if (start >= Length)
                return Empty;

            return new StringView(m_BaseString, m_StartIndex + start, m_EndIndex);
        }

        IStringView IStringView.Substring(int start)
        {
            return Substring(start);
        }

        public StringView Substring(int start, int length)
        {
            if (start < 0 || start >= Length)
                throw new ArgumentException("Index out of string range", nameof(start));

            var innerStartIndex = m_StartIndex + start;
            if (innerStartIndex >= m_EndIndex)
                throw new ArgumentException("Index out of string range", nameof(length));

            return new StringView(m_BaseString, innerStartIndex, innerStartIndex + length);
        }

        IStringView IStringView.Substring(int start, int length)
        {
            return Substring(start, length);
        }

        public StringView Trim(params char[] chrs)
        {
            FindTrimStartEnd(m_StartIndex, m_EndIndex, m_BaseString, chrs, out var start, out var end);
            return new StringView(m_BaseString, start, end);
        }

        IStringView IStringView.Trim(params char[] chrs)
        {
            return Trim(chrs);
        }

        static void FindTrimStartEnd(int localStart, int localEnd, string baseString, char[] chrs, out int trimStart, out int trimEnd)
        {
            trimStart = localStart;
            trimEnd = localEnd;
            for (; trimStart < localEnd;)
            {
                var globalIndex = trimStart;
                var c = baseString[globalIndex];
                if ((chrs != null && chrs.Length > 0 && Array.IndexOf(chrs, c) != -1) || char.IsWhiteSpace(c))
                    trimStart++;
                else
                    break;
            }

            for (; trimEnd > trimStart;)
            {
                var globalIndex = trimEnd - 1;
                var c = baseString[globalIndex];
                if ((chrs != null && chrs.Length > 0 && Array.IndexOf(chrs, c) != -1) || char.IsWhiteSpace(c))
                    trimEnd--;
                else
                    break;
            }
        }

        public int IndexOf(IStringView other, StringComparison sc = StringComparison.Ordinal)
        {
            if (!valid || !other.valid)
                return -1;
            if (Length < other.Length)
                return -1;

            int foundStartIndex = -1;
            int otherIndex = 0;
            for (var i = 0; i < Length && otherIndex < other.Length; ++i)
            {
                if (!Compare(this[i], other[otherIndex], sc))
                {
                    if (foundStartIndex > -1)
                    {
                        foundStartIndex = -1;
                        otherIndex = 0;
                    }
                }
                else
                {
                    if (foundStartIndex == -1)
                        foundStartIndex = i;
                    otherIndex++;
                }
            }

            if (otherIndex != other.Length)
                return -1;
            return foundStartIndex;
        }

        public int IndexOf(StringView other, StringComparison sc = StringComparison.Ordinal)
        {
            return IndexOf(this, other, sc);
        }

        public int IndexOf(string other, StringComparison sc = StringComparison.Ordinal)
        {
            return IndexOf(new StringView(other), sc);
        }

        public int IndexOf(char other, StringComparison sc = StringComparison.Ordinal)
        {
            return IndexOf(this, other, sc);
        }

        static int IndexOf(StringView source, StringView other, StringComparison sc)
        {
            if (!source.valid || !other.valid)
                return -1;
            if (source.Length < other.Length)
                return -1;

            int foundStartIndex = -1;
            int otherIndex = 0;
            for (var i = 0; i < source.Length && otherIndex < other.Length; ++i)
            {
                if (!Compare(source[i], other[otherIndex], sc))
                {
                    if (foundStartIndex > -1)
                    {
                        foundStartIndex = -1;
                        otherIndex = 0;
                    }
                }
                else
                {
                    if (foundStartIndex == -1)
                        foundStartIndex = i;
                    otherIndex++;
                }
            }

            if (otherIndex != other.Length)
                return -1;
            return foundStartIndex;
        }

        static int IndexOf(StringView source, char other, StringComparison sc)
        {
            if (!source.valid)
                return -1;

            for (var i = 0; i < source.Length; ++i)
            {
                if (Compare(source[i], other, sc))
                    return i;
            }

            return -1;
        }

        public int LastIndexOf(IStringView other, StringComparison sc = StringComparison.Ordinal)
        {
            if (Length < other.Length)
                return -1;

            int otherIndex = other.Length - 1;
            for (var i = Length - 1; i >= 0 && otherIndex >= 0; --i)
            {
                if (!Compare(this[i], other[otherIndex], sc))
                {
                    otherIndex = other.Length - 1;
                }
                else
                {
                    if (otherIndex == 0)
                        return i;
                    otherIndex--;
                }
            }

            return -1;
        }

        public int LastIndexOf(StringView other, StringComparison sc = StringComparison.Ordinal)
        {
            return LastIndexOf(this, other, sc);
        }

        public int LastIndexOf(string other, StringComparison sc = StringComparison.Ordinal)
        {
            return LastIndexOf(this, new StringView(other), sc);
        }

        public int LastIndexOf(char other, StringComparison sc = StringComparison.Ordinal)
        {
            return LastIndexOf(this, other, sc);
        }

        static int LastIndexOf(StringView source, StringView other, StringComparison sc)
        {
            if (source.Length < other.Length)
                return -1;

            int otherIndex = other.Length - 1;
            for (var i = source.Length - 1; i >= 0 && otherIndex >= 0; --i)
            {
                if (!Compare(source[i], other[otherIndex], sc))
                {
                    otherIndex = other.Length - 1;
                }
                else
                {
                    if (otherIndex == 0)
                        return i;
                    otherIndex--;
                }
            }

            return -1;
        }

        static int LastIndexOf(StringView source, char other, StringComparison sc)
        {
            if (!source.valid)
                return -1;

            for (var i = source.Length - 1; i >= 0; --i)
            {
                if (Compare(source[i], other, sc))
                    return i;
            }

            return -1;
        }

        public bool StartsWith(char c, StringComparison stringComparison = StringComparison.Ordinal)
        {
            if (Length == 0)
                return false;
            return Compare(this[0], c, stringComparison);
        }

        public bool StartsWith(string v, StringComparison sc = StringComparison.Ordinal)
        {
            return StartsWith(this, new StringView(v), sc);
        }

        public bool StartsWith(IStringView v, StringComparison sc = StringComparison.Ordinal)
        {
            if (v.Length > Length)
                return false;

            for (var i = 0; i < v.Length; ++i)
                if (!Compare(this[i], v[i], sc))
                    return false;

            return true;
        }

        static bool StartsWith(StringView source, StringView other, StringComparison sc)
        {
            if (other.Length > source.Length)
                return false;

            for (var i = 0; i < other.Length; ++i)
                if (!Compare(source[i], other[i], sc))
                    return false;

            return true;
        }

        public bool EndsWith(char c, StringComparison sc = StringComparison.Ordinal)
        {
            if (Length == 0)
                return false;
            return Compare(this[Length - 1], c, sc);
        }

        public bool EndsWith(string v, StringComparison sc = StringComparison.Ordinal)
        {
            return EndsWith(this, new StringView(v), sc);
        }

        public bool EndsWith(IStringView v, StringComparison sc = StringComparison.Ordinal)
        {
            if (v.Length > Length)
                return false;

            for (var i = 0; i < v.Length; ++i)
                if (!Compare(this[Length - v.Length + i], v[i], sc))
                    return false;

            return true;
        }

        static bool EndsWith(StringView source, StringView other, StringComparison sc)
        {
            if (other.Length > source.Length)
                return false;

            for (var i = 0; i < other.Length; ++i)
                if (!Compare(source[source.Length - other.Length + i], other[i], sc))
                    return false;

            return true;
        }

        public bool Contains(char c, StringComparison ordinal = StringComparison.Ordinal)
        {
            for (var i = 0; i < Length; ++i)
                if (Compare(this[i], c, ordinal))
                    return true;
            return false;
        }

        public bool Contains(StringView s, StringComparison ordinal = StringComparison.Ordinal)
        {
            return IndexOf(s) != -1;
        }

        public bool Contains(IStringView s, StringComparison ordinal = StringComparison.Ordinal)
        {
            return IndexOf(s) != -1;
        }

        public bool Contains(string s, StringComparison ordinal = StringComparison.Ordinal)
        {
            return IndexOf(s) != -1;
        }

        public static bool operator==(StringView lhs, StringView rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator!=(StringView lhs, StringView rhs)
        {
            return !lhs.Equals(rhs);
        }

        public static bool operator==(StringView lhs, string rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator!=(StringView lhs, string rhs)
        {
            return !lhs.Equals(rhs);
        }

        public static bool operator==(string lhs, StringView rhs)
        {
            return rhs.Equals(lhs);
        }

        public static bool operator!=(string lhs, StringView rhs)
        {
            return !rhs.Equals(lhs);
        }

        public static bool operator==(StringView lhs, IStringView rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator!=(StringView lhs, IStringView rhs)
        {
            return !lhs.Equals(rhs);
        }

        public static bool operator==(IStringView lhs, StringView rhs)
        {
            return rhs.Equals(lhs);
        }

        public static bool operator!=(IStringView lhs, StringView rhs)
        {
            return !rhs.Equals(lhs);
        }

        public static implicit operator bool(StringView sv)
        {
            return !sv.IsNullOrEmpty();
        }

        public override int GetHashCode()
        {
            var hc = 0;
            for (var i = m_StartIndex; i < m_EndIndex; ++i)
                hc ^= m_BaseString[i].GetHashCode();
            return hc;
        }

        public override string ToString()
        {
            if (!valid)
                return string.Empty;
            if (m_StartIndex == 0 && Length == m_BaseString.Length)
                return m_BaseString;
            return m_BaseString.Substring(m_StartIndex, Length);
        }

        public IEnumerator<char> GetEnumerator()
        {
            for (var i = m_StartIndex; i < m_EndIndex; ++i)
                yield return m_BaseString[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal static bool Compare(char c1, char c2, StringComparison sc)
        {
            switch (sc)
            {
                case StringComparison.OrdinalIgnoreCase:
                case StringComparison.CurrentCultureIgnoreCase:
                    return char.ToLower(c1) == char.ToLower(c2);
                case StringComparison.InvariantCultureIgnoreCase:
                    return char.ToLowerInvariant(c1) == char.ToLowerInvariant(c2);
                default:
                    return c1 == c2;
            }
        }

        static int GetGlobalIndex(int localIndex)
        {
            return localIndex;
        }
    }

    static partial class StringExtensions
    {
        static readonly char[] k_OneLetterWords = new char[] { ':', '-', '!' };
        static readonly char[] k_WordSplitters = new char[] { '(', ')', '{', '}', '[', ']', ':', '-' };

        public static StringView GetStringView(this string baseString, int startIndex)
        {
            return new StringView(baseString, startIndex, baseString.Length);
        }

        public static StringView GetStringView(this string baseString, int startIndex, int endIndex)
        {
            return new StringView(baseString, startIndex, endIndex);
        }

        public static StringView GetStringView(this string baseString)
        {
            return new StringView(baseString);
        }

        public static bool IsNullOrEmpty(this StringView sv)
        {
            if (!sv.valid)
                return true;
            return sv.Length == 0;
        }

        public static bool IsNullOrWhiteSpace(this StringView sv)
        {
            if (sv.IsNullOrEmpty())
                return true;
            for (var i = 0; i < sv.Length; ++i)
            {
                if (!char.IsWhiteSpace(sv[i]))
                    return false;
            }
            return true;
        }

        public static StringView GetWordView(this string baseString, int startIndex)
        {
            if (startIndex < 0 || startIndex >= baseString.Length)
                throw new ArgumentException("Index out of string range", nameof(startIndex));

            var i = startIndex;
            if (Array.IndexOf(k_OneLetterWords, baseString[i]) != -1)
                return new StringView(baseString, startIndex, i + 1);

            var lod = char.IsLetterOrDigit(baseString[i++]);
            while (i < baseString.Length)
            {
                if (Array.IndexOf(k_WordSplitters, baseString[i]) != -1)
                    break;

                if (lod == char.IsLetterOrDigit(baseString[i]))
                    i++;
                else
                    break;
            }
            return new StringView(baseString, startIndex, i);
        }

        public static bool HasQuotes(this StringView sv, in char quoteChar = '"')
        {
            return sv.Length >= 2 && sv.StartsWith(quoteChar) && sv.EndsWith(quoteChar);
        }
    }
}
