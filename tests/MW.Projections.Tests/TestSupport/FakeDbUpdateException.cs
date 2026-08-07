namespace MW.Projections.Tests.TestSupport;

/// <summary>
/// Deliberately named <c>DbUpdateException</c> (not the real EF Core type — MW.Projections must
/// not reference EF Core, and neither may this test project) so tests can exercise
/// <c>ProjectionReconcilerBase.IsDuplicateInsertFailure</c>'s runtime-type-NAME check without a
/// package reference to <c>Microsoft.EntityFrameworkCore</c>.
/// </summary>
internal sealed class DbUpdateException : Exception
{
    public DbUpdateException(string message) : base(message)
    {
    }
}
