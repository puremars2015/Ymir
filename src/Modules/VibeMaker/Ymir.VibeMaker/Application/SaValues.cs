using System.Text;

namespace Ymir.VibeMaker.Application;

/// <summary>對外的狀態值與資料庫相同，使用 SA 的大寫格式（例如 <c>NOT_CREATED</c>）。</summary>
internal static class SaValues
{
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(name[i]));
        }

        return builder.ToString();
    }
}
