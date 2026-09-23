namespace PrepHub.Domain.Common;

/// <summary>
/// Base para todas as entidades do domínio. A identidade é dada por <see cref="Id"/> (Guid),
/// gerado no próprio domínio para não depender da estratégia de chave do provider de banco
/// (facilita a eventual migração SQLite -> PostgreSQL).
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        if (id != Guid.Empty)
        {
            Id = id;
        }
    }

    public override bool Equals(object? obj)
        => obj is Entity other && GetType() == other.GetType() && Id == other.Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
