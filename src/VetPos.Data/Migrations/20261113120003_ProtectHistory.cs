using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetPos.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtectHistory : Migration
    {
        /// <summary>
        /// History is protected by the database itself, so not even a bug or a hand-typed SQL
        /// command can quietly change or remove a bill, a payment, a stock movement or the audit log.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AppendOnly)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "{table}_no_update" BEFORE UPDATE ON "{table}"
                    BEGIN SELECT RAISE(ABORT, '{table} rows are history and cannot be changed'); END;
                    """);
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "{table}_no_delete" BEFORE DELETE ON "{table}"
                    BEGIN SELECT RAISE(ABORT, '{table} rows are history and cannot be deleted'); END;
                    """);
            }

            // Bills are voided, never deleted; once saved their money and lines never change.
            migrationBuilder.Sql("""
                CREATE TRIGGER "Sales_no_delete" BEFORE DELETE ON "Sales"
                BEGIN SELECT RAISE(ABORT, 'Bills cannot be deleted; void them instead'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "Sales_money_fixed"
                BEFORE UPDATE OF "ClientId", "Number", "CashierId", "Subtotal", "LineDiscountTotal",
                    "BillDiscountType", "BillDiscountValue", "BillDiscountAmount", "Total", "Paid", "Change",
                    "CostTotal", "CreatedAt" ON "Sales"
                BEGIN SELECT RAISE(ABORT, 'A saved bill cannot be changed; void it instead'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "Sales_void_once" BEFORE UPDATE OF "Status" ON "Sales"
                WHEN OLD."Status" = 1
                BEGIN SELECT RAISE(ABORT, 'This bill is already voided'); END;
                """);
        }

        private static readonly string[] AppendOnly =
            ["SaleLines", "SaleLineBatches", "Payments", "StockMovements", "AuditLog"];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AppendOnly)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS \"{table}_no_update\";");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS \"{table}_no_delete\";");
            }
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"Sales_no_delete\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"Sales_money_fixed\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"Sales_void_once\";");
        }
    }
}
