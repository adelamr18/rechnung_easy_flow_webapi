using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceEasy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    MerchantName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UploadDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExtractedData = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Receipts");
        }
    }
}
