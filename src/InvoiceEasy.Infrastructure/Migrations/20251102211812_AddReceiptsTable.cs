using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceEasy.Infrastructure.Migrations
{
    public partial class AddReceiptsTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExtractedData",
                table: "Receipts",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldDefaultValueSql: "'{}'::jsonb");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExtractedData",
                table: "Receipts",
                type: "jsonb",
                nullable: true,
                defaultValueSql: "'{}'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}
