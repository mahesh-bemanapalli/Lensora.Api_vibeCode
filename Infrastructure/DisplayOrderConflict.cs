using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Lensora.Api.Infrastructure;

public static class DisplayOrderConflict
{
    public const string Message = "This display order is already used in this section. Choose another number.";

    public static bool IsUniqueIndexViolation(DbUpdateException exception) =>
        exception.GetBaseException() is SqlException sql && sql.Number is 2601 or 2627;
}
