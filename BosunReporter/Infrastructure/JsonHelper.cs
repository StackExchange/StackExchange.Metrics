using System.Text;

namespace BosunReporter.Infrastructure
{
    static class JsonHelper
    {
        internal static void WriteString(StringBuilder sb, string s)
        {
            if (s == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('\"');

            foreach (var c in s)
            {
                if (c == '\\')
                {
                    sb.Append("\\\\");
                    continue;
                }

                switch (c)
                {
                    case '\u0000':
                        sb.Append("\\u0000");
                        break;
                    case '\u0001':
                        sb.Append("\\u0001");
                        break;
                    case '\u0002':
                        sb.Append("\\u0002");
                        break;
                    case '\u0003':
                        sb.Append("\\u0003");
                        break;
                    case '\u0004':
                        sb.Append("\\u0004");
                        break;
                    case '\u0005':
                        sb.Append("\\u0005");
                        break;
                    case '\u0006':
                        sb.Append("\\u0006");
                        break;
                    case '\u0007':
                        sb.Append("\\u0007");
                        break;
                    case '\u0008':
                        sb.Append("\\b");
                        break;
                    case '\u0009':
                        sb.Append("\\t");
                        break;
                    case '\u000a':
                        sb.Append("\\n");
                        break;
                    case '\u000b':
                        sb.Append("\\u000b");
                        break;
                    case '\u000c':
                        sb.Append("\\f");
                        break;
                    case '\u000d':
                        sb.Append("\\r");
                        break;
                    case '\u000e':
                        sb.Append("\\u000e");
                        break;
                    case '\u000f':
                        sb.Append("\\u000f");
                        break;
                    case '\u0010':
                        sb.Append("\\u0010");
                        break;
                    case '\u0011':
                        sb.Append("\\u0011");
                        break;
                    case '\u0012':
                        sb.Append("\\u0012");
                        break;
                    case '\u0013':
                        sb.Append("\\u0013");
                        break;
                    case '\u0014':
                        sb.Append("\\u0014");
                        break;
                    case '\u0015':
                        sb.Append("\\u0015");
                        break;
                    case '\u0016':
                        sb.Append("\\u0016");
                        break;
                    case '\u0017':
                        sb.Append("\\u0017");
                        break;
                    case '\u0018':
                        sb.Append("\\u0018");
                        break;
                    case '\u0019':
                        sb.Append("\\u0019");
                        break;
                    case '\u001a':
                        sb.Append("\\u001a");
                        break;
                    case '\u001b':
                        sb.Append("\\u001b");
                        break;
                    case '\u001c':
                        sb.Append("\\u001c");
                        break;
                    case '\u001d':
                        sb.Append("\\u001d");
                        break;
                    case '\u001e':
                        sb.Append("\\u001e");
                        break;
                    case '\u001f':
                        sb.Append("\\u001f");
                        break;
                    case '\u0020':
                    case '\u0021':
                        goto default;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            sb.Append('\"');
        }
    }
}