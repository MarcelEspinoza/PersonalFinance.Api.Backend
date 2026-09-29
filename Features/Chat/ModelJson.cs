using System.Text;

namespace PersonalFinance.Api.Features.Chat
{
    /// <summary>
    /// Los modelos devuelven a veces el JSON envuelto en markdown, precedido de
    /// una frase, o cortado a la mitad cuando se agota "max_tokens". Este lector
    /// recupera el objeto JSON en esos casos en lugar de fallar y dejar al
    /// usuario con un mensaje de error genérico.
    /// </summary>
    public static class ModelJson
    {
        /// <summary>
        /// Devuelve el primer objeto JSON del texto, cerrando las comillas y
        /// llaves que falten si la respuesta llegó truncada. Si no hay ningún
        /// objeto, devuelve el texto limpio de markdown.
        /// </summary>
        public static string Extract(string? raw)
        {
            var text = StripCodeFence(raw ?? string.Empty);
            var start = text.IndexOf('{');
            if (start < 0) return text;

            var closers = new Stack<char>();
            var inString = false;
            var escaped = false;

            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];

                if (escaped) { escaped = false; continue; }

                if (inString)
                {
                    if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        closers.Push('}');
                        break;
                    case '[':
                        closers.Push(']');
                        break;
                    case '}':
                    case ']':
                        if (closers.Count > 0) closers.Pop();
                        if (closers.Count == 0) return text[start..(i + 1)];
                        break;
                }
            }

            return Repair(text[start..], closers, inString, escaped);
        }

        /// <summary>
        /// Texto legible para el usuario cuando la respuesta no era JSON: se le
        /// enseña lo que dijo el modelo en vez de un error opaco.
        /// </summary>
        public static string PlainText(string? raw) => StripCodeFence(raw ?? string.Empty);

        private static string Repair(string fragment, Stack<char> closers, bool inString, bool escaped)
        {
            var builder = new StringBuilder(fragment.TrimEnd());
            if (escaped && builder.Length > 0) builder.Length--;
            if (inString) builder.Append('"');

            // Una propiedad a medias ("reply": o una coma final) rompería el
            // objeto reparado, así que se completa o se descarta.
            while (builder.Length > 0 && (builder[^1] == ',' || builder[^1] == ':'))
            {
                if (builder[^1] == ':')
                {
                    builder.Append("null");
                    break;
                }
                builder.Length--;
            }

            while (closers.Count > 0) builder.Append(closers.Pop());
            return builder.ToString();
        }

        private static string StripCodeFence(string raw)
        {
            var text = raw.Trim();
            var fence = text.IndexOf("```", StringComparison.Ordinal);
            if (fence < 0) return text;

            var afterFence = text[(fence + 3)..];
            var firstNewLine = afterFence.IndexOf('\n');
            if (firstNewLine >= 0) afterFence = afterFence[(firstNewLine + 1)..];

            var closing = afterFence.IndexOf("```", StringComparison.Ordinal);
            if (closing >= 0) afterFence = afterFence[..closing];

            return afterFence.Trim();
        }
    }
}
