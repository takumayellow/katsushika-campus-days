using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KCD
{
    /// <summary>MiniJson の字句解析。文字列を読み進めて辞書・配列・数値へ組み立てる。</summary>
    public static partial class MiniJson
    {
        private sealed class Parser
        {
            private const int MaxDepth = 64;
            private readonly string _source;
            private int _index;
            private int _depth;

            public Parser(string source)
            {
                _source = source;
                _index = 0;
            }

            public void SkipWhitespace()
            {
                while (_index < _source.Length && char.IsWhiteSpace(_source[_index]))
                {
                    _index++;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (_index >= _source.Length)
                {
                    throw new FormatException("JSON が途中で終了しました。");
                }

                char c = _source[_index];
                switch (c)
                {
                    case '{': return Nested(ParseObject);
                    case '[': return Nested(ParseArray);
                    case '"': return ParseString();
                    case 't': return ParseLiteral("true", true);
                    case 'f': return ParseLiteral("false", false);
                    case 'n': return ParseLiteral("null", null);
                    default: return ParseNumber();
                }
            }

            /// <summary>入れ子の深さを数え、上限を超えたら StackOverflow の前に FormatException で止める。</summary>
            private object Nested(Func<object> parse)
            {
                if (_depth >= MaxDepth)
                {
                    throw new FormatException("JSON の入れ子が深すぎます (上限 " + MaxDepth + ")。");
                }

                _depth++;
                try
                {
                    return parse();
                }
                finally
                {
                    _depth--;
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                _index++; // '{'
                SkipWhitespace();

                if (_index < _source.Length && _source[_index] == '}')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();

                    if (_index >= _source.Length)
                    {
                        throw new FormatException("オブジェクトが閉じられていません。");
                    }

                    if (_source[_index] == ',')
                    {
                        _index++;
                        continue;
                    }

                    Expect('}');
                    return result;
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                _index++; // '['
                SkipWhitespace();

                if (_index < _source.Length && _source[_index] == ']')
                {
                    _index++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();

                    if (_index >= _source.Length)
                    {
                        throw new FormatException("配列が閉じられていません。");
                    }

                    if (_source[_index] == ',')
                    {
                        _index++;
                        continue;
                    }

                    Expect(']');
                    return result;
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();

                while (_index < _source.Length)
                {
                    char c = _source[_index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (_index >= _source.Length)
                    {
                        break;
                    }

                    char escaped = _source[_index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_index + 4 <= _source.Length)
                            {
                                string hex = _source.Substring(_index, 4);
                                _index += 4;
                                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                {
                                    throw new FormatException("不正な \\u エスケープ: " + hex);
                                }

                                builder.Append((char)code);
                            }
                            else
                            {
                                throw new FormatException("\\u エスケープが途中で終了しました。");
                            }

                            break;
                        default: builder.Append(escaped); break;
                    }
                }

                throw new FormatException("文字列が閉じられていません。");
            }

            private object ParseNumber()
            {
                int start = _index;
                while (_index < _source.Length && "+-0123456789.eE".IndexOf(_source[_index]) >= 0)
                {
                    _index++;
                }

                string text = _source.Substring(start, _index - start);
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    return value;
                }

                throw new FormatException("数値として解釈できません: " + text);
            }

            private object ParseLiteral(string literal, object value)
            {
                if (_index + literal.Length > _source.Length ||
                    string.CompareOrdinal(_source, _index, literal, 0, literal.Length) != 0)
                {
                    throw new FormatException("予期しないリテラルです。");
                }

                _index += literal.Length;
                return value;
            }

            private void Expect(char expected)
            {
                if (_index >= _source.Length || _source[_index] != expected)
                {
                    throw new FormatException("'" + expected + "' が必要です。");
                }

                _index++;
            }
        }
    }
}
