using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AegisScribe.Domain.Data;

// Recognising the constraint that refused a write, so a repository can turn it into the domain
// exception the caller already knows how to handle.
//
// Several rules in this repo are enforced by an index or a foreign key rather than only by a check in
// Business, deliberately, because a check-then-write is a race (see
// RosterEntryRepository.TryLinkAltAsync). The database winning that race is the DESIGN working; the
// caller seeing a 500 because nobody translated the violation is the design leaking. Every constraint
// that backs a domain rule needs a line here.
internal static class SqlConstraintViolations
{
    // 2627 is a unique-constraint violation, 2601 a unique-index one; SQL Server names the index in
    // the message for both.
    private const int UniqueConstraint = 2627;
    private const int UniqueIndex = 2601;

    // 547 covers both FK and CHECK violations; the constraint name in the message distinguishes them.
    private const int ConstraintConflict = 547;

    /// <summary>
    /// Whether <paramref name="exception"/> is a uniqueness violation on <paramref name="indexName"/>.
    /// </summary>
    /// <remarks>
    /// Matching on the index NAME is the load-bearing part: these helpers run on every write through a
    /// repository, and a DIFFERENT unique index breaking must not be reported to the caller as
    /// whichever domain rule this call site happens to be about.
    /// </remarks>
    public static bool IsUniqueViolationOn(this DbUpdateException exception, string indexName) =>
        exception.InnerException is SqlException { Number: UniqueIndex or UniqueConstraint } sql
        && sql.Message.Contains(indexName, StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="exception"/> is a foreign-key violation on
    /// <paramref name="constraintName"/>.
    /// </summary>
    public static bool IsForeignKeyViolationOn(this DbUpdateException exception, string constraintName) =>
        exception.InnerException is SqlException { Number: ConstraintConflict } sql
        && sql.Message.Contains(constraintName, StringComparison.Ordinal);
}
