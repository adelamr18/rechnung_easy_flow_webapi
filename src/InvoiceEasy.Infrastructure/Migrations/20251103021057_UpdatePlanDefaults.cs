using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceEasy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePlanDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Plan",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "starter",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "free");

            migrationBuilder.Sql("UPDATE \"Users\" SET \"Plan\" = 'starter' WHERE \"Plan\" = 'free';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Plan",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "free",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "starter");

            migrationBuilder.Sql("UPDATE \"Users\" SET \"Plan\" = 'free' WHERE \"Plan\" = 'starter';");
        }
    }
}
