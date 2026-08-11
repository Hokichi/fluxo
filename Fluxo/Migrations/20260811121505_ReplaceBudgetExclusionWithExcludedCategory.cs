using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fluxo.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceBudgetExclusionWithExcludedCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Transactions"
                SET "ExpenseCategory" = CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "Transactions" AS "Child"
                        WHERE "Child"."ParentTransactionId" = "Transactions"."Id") THEN NULL
                    WHEN "IsExcludedFromBudget" = 1 OR "Type" = 1 OR "IsIoU" = 1 OR
                         "GoalId" IS NOT NULL OR "RepaymentAccountId" IS NOT NULL THEN 4
                    ELSE "ExpenseCategory"
                END;

                UPDATE "RecurringTransactions"
                SET "Category" = 4
                WHERE "IsExcludedFromBudget" = 1 OR "Type" IN (2, 3);
                """);

            migrationBuilder.DropColumn(
                name: "IsExcludedFromBudget",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsExcludedFromBudget",
                table: "RecurringTransactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExcludedFromBudget",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsExcludedFromBudget",
                table: "RecurringTransactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "Transactions"
                SET "IsExcludedFromBudget" = 1,
                    "ExpenseCategory" = CASE
                        WHEN "Type" = 1 THEN NULL
                        WHEN "GoalId" IS NOT NULL THEN 3
                        ELSE 1
                    END
                WHERE "ExpenseCategory" = 4;

                UPDATE "RecurringTransactions"
                SET "IsExcludedFromBudget" = 1,
                    "Category" = CASE
                        WHEN "Type" = 2 THEN NULL
                        WHEN "Type" = 3 THEN 3
                        ELSE 1
                    END
                WHERE "Category" = 4;
                """);
        }
    }
}
