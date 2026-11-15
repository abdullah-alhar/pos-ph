using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetPos.Data.Migrations
{
    /// <inheritdoc />
    public partial class MedicineDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DosageForm",
                table: "Products",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenericName",
                table: "Products",
                type: "TEXT",
                maxLength: 120,
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.AddColumn<bool>(
                name: "IsMedicine",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Species",
                table: "Products",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Strength",
                table: "Products",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_GenericName",
                table: "Products",
                column: "GenericName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_GenericName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DosageForm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "GenericName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsMedicine",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Species",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Strength",
                table: "Products");
        }
    }
}
