namespace PrepHub.Domain.Common;

/// <summary>
/// Validações defensivas simples para invariantes de domínio.
/// Mantém os construtores das entidades enxutos e as regras num só lugar.
/// </summary>
internal static class Guard
{
    public static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Valor não pode ser vazio.", paramName);
        }

        return value.Trim();
    }

    public static Guid NotEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identificador não pode ser vazio.", paramName);
        }

        return value;
    }

    public static int Positive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Valor deve ser maior que zero.");
        }

        return value;
    }

    public static int InRange(int value, int min, int max, string paramName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Valor deve estar entre {min} e {max}.");
        }

        return value;
    }

    public static decimal InRange(decimal value, decimal min, decimal max, string paramName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Valor deve estar entre {min} e {max}.");
        }

        return value;
    }
}
