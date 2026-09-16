using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindAeonRest.Licence.Data.Migrations
{
    /// <inheritdoc />
    public partial class Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Businesses_BusinessId",
                table: "Licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Dealers_DealerId",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_UsageReports_CompanyCode_Period",
                table: "UsageReports");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_LicenseCode",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_CompanyCode",
                table: "Businesses");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "UsageReports",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "UsageReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "UsageReports",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "UsageReports",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "UsageReports",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OpenerUserId",
                table: "Tickets",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AssignedToUserId",
                table: "Tickets",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Tickets",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "Tickets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Tickets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Tickets",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "Tickets",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AuthorUserId",
                table: "TicketMessages",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "TicketMessages",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "TicketMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TicketMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TicketMessages",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "TicketMessages",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Payments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Payments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Payments",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "Payments",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "PaymentProviderConfigs",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "PaymentProviderConfigs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PaymentProviderConfigs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PaymentProviderConfigs",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "PaymentProviderConfigs",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "PackagePrices",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "PackagePrices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PackagePrices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PackagePrices",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "PackagePrices",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IssuedByUserId",
                table: "Licenses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Licenses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "Licenses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Licenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Licenses",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "Licenses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "LicenseRequests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "LicenseRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "LicenseRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "LicenseRequests",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "LicenseRequests",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ByUserId",
                table: "LicenseHistories",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "LicenseHistories",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "LicenseHistories",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "LicenseHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "LicenseHistories",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "LicenseHistories",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "DemoInstalls",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "DemoInstalls",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "DemoInstalls",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "DemoInstalls",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "DemoInstalls",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MarginPercent",
                table: "Dealers",
                type: "decimal(5,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Dealers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "Dealers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Dealers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Dealers",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "Dealers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Businesses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "Businesses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Businesses",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedByUserId",
                table: "Businesses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageReports_BusinessId_Period",
                table: "UsageReports",
                columns: new[] { "BusinessId", "Period" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Period] IS NOT NULL AND [BusinessId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Provider_ProviderRef",
                table: "Payments",
                columns: new[] { "Provider", "ProviderRef" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ProviderRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_LicenseCode",
                table: "Licenses",
                column: "LicenseCode",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_CompanyCode",
                table: "Businesses",
                column: "CompanyCode",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Businesses_BusinessId",
                table: "Licenses",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Dealers_DealerId",
                table: "Licenses",
                column: "DealerId",
                principalTable: "Dealers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Businesses_BusinessId",
                table: "Licenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_Dealers_DealerId",
                table: "Licenses");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_UsageReports_BusinessId_Period",
                table: "UsageReports");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Provider_ProviderRef",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_LicenseCode",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_CompanyCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "UsageReports");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "UsageReports");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "UsageReports");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "UsageReports");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "UsageReports");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "TicketMessages");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "TicketMessages");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TicketMessages");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TicketMessages");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "TicketMessages");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "PaymentProviderConfigs");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "PaymentProviderConfigs");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PaymentProviderConfigs");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PaymentProviderConfigs");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "PaymentProviderConfigs");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "PackagePrices");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "PackagePrices");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PackagePrices");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PackagePrices");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "PackagePrices");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "LicenseRequests");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "LicenseRequests");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "LicenseRequests");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "LicenseRequests");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "LicenseRequests");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "LicenseHistories");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "LicenseHistories");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "LicenseHistories");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "LicenseHistories");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "LicenseHistories");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "DemoInstalls");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "DemoInstalls");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "DemoInstalls");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "DemoInstalls");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "DemoInstalls");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Dealers");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "Dealers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Dealers");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Dealers");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Dealers");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "Businesses");

            migrationBuilder.AlterColumn<string>(
                name: "OpenerUserId",
                table: "Tickets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedToUserId",
                table: "Tickets",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AuthorUserId",
                table: "TicketMessages",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "IssuedByUserId",
                table: "Licenses",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ByUserId",
                table: "LicenseHistories",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MarginPercent",
                table: "Dealers",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)");

            migrationBuilder.CreateIndex(
                name: "IX_UsageReports_CompanyCode_Period",
                table: "UsageReports",
                columns: new[] { "CompanyCode", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_LicenseCode",
                table: "Licenses",
                column: "LicenseCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_CompanyCode",
                table: "Businesses",
                column: "CompanyCode");

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Businesses_BusinessId",
                table: "Licenses",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_Dealers_DealerId",
                table: "Licenses",
                column: "DealerId",
                principalTable: "Dealers",
                principalColumn: "Id");
        }
    }
}
