using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ymir.Platform.Infrastructure.Persistence;

/// <summary>資料表 / 欄位一律 snake_case（與 SA §8 一致）；enum 存成大寫字串（例如 <c>QUEUED</c>）。</summary>
public static class NamingConventions
{
    public static void ApplySnakeCaseNames(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? string.Empty));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? string.Empty));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName() ?? string.Empty));
            }
        }
    }

    public static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

/// <summary>Enum ⇄ 大寫字串（<c>NotCreated</c> → <c>NOT_CREATED</c>）。</summary>
public sealed class UpperSnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => NamingConventions.ToSnakeCase(value.ToString()).ToUpperInvariant(),
    value => Enum.Parse<TEnum>(value.Replace("_", string.Empty, StringComparison.Ordinal), true))
    where TEnum : struct, Enum;
