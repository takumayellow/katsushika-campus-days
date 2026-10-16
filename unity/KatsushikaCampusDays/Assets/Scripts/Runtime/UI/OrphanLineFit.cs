using TMPro;

namespace KCD
{
    /// <summary>
    /// 折り返した段落の最後の行が 1〜2 文字だけになるとき（「…キャンパスに入」「る」）、
    /// 文字を少しずつ小さくして前の行に収める (#187)。小さくしすぎるくらいなら元の大きさのまま折り返す。
    /// </summary>
    public static class OrphanLineFit
    {
        /// <summary>この文字数以下の最後の行を、取り残された行とみなす。</summary>
        public const int OrphanChars = 2;

        /// <summary>元の大きさに対して、ここまでは小さくしてよい。</summary>
        public const float MinScale = 0.85f;

        /// <summary>
        /// label を maxSize から 1 ずつ小さくし、取り残された行が無くなった大きさで止める。
        /// 自動の大きさ合わせ（enableAutoSizing）を使う文字は、その上限を動かす。
        /// </summary>
        public static void Apply(TMP_Text label, float maxSize)
        {
            if (label == null)
            {
                return;
            }

            float floor = maxSize * MinScale;
            if (label.enableAutoSizing)
            {
                floor = System.Math.Max(floor, label.fontSizeMin);
            }

            for (float size = maxSize; size >= floor; size -= 1f)
            {
                SetSize(label, size);
                if (!HasOrphan(label))
                {
                    return;
                }
            }

            SetSize(label, maxSize);
        }

        /// <summary>いまの形で、折り返して最後の行が OrphanChars 文字以下になった段落があるか。</summary>
        public static bool HasOrphan(TMP_Text label)
        {
            TMP_TextInfo info = label != null ? label.textInfo : null;
            if (info == null)
            {
                return false;
            }

            for (int i = 1; i < info.lineCount; i++)
            {
                TMP_LineInfo line = info.lineInfo[i];
                bool wrapped = !EndsWithBreak(info, info.lineInfo[i - 1]);
                bool paragraphEnd = i == info.lineCount - 1 || EndsWithBreak(info, line);
                if (wrapped && paragraphEnd && line.visibleCharacterCount > 0
                    && line.visibleCharacterCount <= OrphanChars)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EndsWithBreak(TMP_TextInfo info, TMP_LineInfo line)
        {
            int last = line.lastCharacterIndex;
            return last >= 0 && last < info.characterCount && info.characterInfo[last].character == '\n';
        }

        private static void SetSize(TMP_Text label, float size)
        {
            if (label.enableAutoSizing)
            {
                label.fontSizeMax = size;
            }
            else
            {
                label.fontSize = size;
            }

            label.ForceMeshUpdate(true, true);
        }
    }
}
