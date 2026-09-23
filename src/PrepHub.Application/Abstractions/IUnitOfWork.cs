namespace PrepHub.Application.Abstractions;

/// <summary>
/// Confirma as alterações rastreadas na mesma transação lógica.
/// Abstrai o SaveChanges do EF Core para manter a Application independente do provider.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
