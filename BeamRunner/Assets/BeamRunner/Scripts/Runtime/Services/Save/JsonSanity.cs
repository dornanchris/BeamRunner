namespace BeamRunner.Services
{
    /// <summary>
    /// Cheap structural check used to reject truncated or garbage save files when the caller supplies no
    /// stronger validator: the text must be a single JSON object with balanced braces/brackets and
    /// terminated strings. It does not validate numbers or keywords.
    /// </summary>
    public static class JsonSanity
    {
        /// <summary>True when <paramref name="text"/> structurally looks like one complete JSON object.</summary>
        public static bool LooksLikeJsonObject(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int start = 0;
            int end = text.Length - 1;
            while (start <= end && IsWhite(text[start]))
            {
                start++;
            }

            // Tolerate a UTF-8 byte order mark decoded as U+FEFF.
            if (start <= end && text[start] == '﻿')
            {
                start++;
                while (start <= end && IsWhite(text[start]))
                {
                    start++;
                }
            }

            while (end >= start && IsWhite(text[end]))
            {
                end--;
            }

            if (start >= end || text[start] != '{' || text[end] != '}')
            {
                return false;
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            // Explicit stack of opening characters; nesting beyond 256 levels is not a plausible save file.
            var stack = new char[256];

            for (int i = start; i <= end; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    else if (c < ' ')
                    {
                        // Raw control characters are illegal inside JSON strings (usually a torn write).
                        return false;
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                    case '[':
                        if (depth >= stack.Length)
                        {
                            return false;
                        }

                        stack[depth++] = c;
                        break;
                    case '}':
                        if (depth == 0 || stack[--depth] != '{')
                        {
                            return false;
                        }

                        if (depth == 0 && i != end)
                        {
                            return false;
                        }

                        break;
                    case ']':
                        if (depth == 0 || stack[--depth] != '[')
                        {
                            return false;
                        }

                        break;
                    case '\0':
                        return false;
                }
            }

            return depth == 0 && !inString;
        }

        private static bool IsWhite(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }
    }
}
