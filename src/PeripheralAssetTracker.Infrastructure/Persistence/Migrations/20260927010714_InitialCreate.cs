using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeripheralAssetTracker.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Computers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    WindowsVersion = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Computers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResponsiblePeople",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponsiblePeople", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FingerprintConfidence = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    FingerprintBasis = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DeviceType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    VendorId = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    BluetoothAddress = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    BusType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PnpDeviceId = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenComputerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.CheckConstraint("CK_Devices_Confidence", "\"FingerprintConfidence\" IN ('Low', 'Medium', 'High')");
                    table.CheckConstraint("CK_Devices_FingerprintLength", "length(\"Fingerprint\") = 64");
                    table.CheckConstraint("CK_Devices_Type", "\"DeviceType\" IN ('Mouse', 'Keyboard', 'Other')");
                    table.ForeignKey(
                        name: "FK_Devices_Computers_LastSeenComputerId",
                        column: x => x.LastSeenComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponsiblePersonId = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpectedComputerId = table.Column<int>(type: "INTEGER", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                    table.CheckConstraint("CK_Assignments_ActiveState", "(\"IsActive\" = 1 AND \"EndedAt\" IS NULL) OR (\"IsActive\" = 0 AND \"EndedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_Assignments_Target", "\"ResponsiblePersonId\" IS NOT NULL OR \"ExpectedComputerId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Assignments_Computers_ExpectedComputerId",
                        column: x => x.ExpectedComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assignments_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assignments_ResponsiblePeople_ResponsiblePersonId",
                        column: x => x.ResponsiblePersonId,
                        principalTable: "ResponsiblePeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                    MovementType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FromResponsiblePersonId = table.Column<int>(type: "INTEGER", nullable: true),
                    ToResponsiblePersonId = table.Column<int>(type: "INTEGER", nullable: true),
                    FromComputerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ToComputerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceMovements", x => x.Id);
                    table.CheckConstraint("CK_DeviceMovements_Type", "\"MovementType\" IN ('Assignment', 'Transfer')");
                    table.ForeignKey(
                        name: "FK_DeviceMovements_Computers_FromComputerId",
                        column: x => x.FromComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceMovements_Computers_ToComputerId",
                        column: x => x.ToComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceMovements_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceMovements_ResponsiblePeople_FromResponsiblePersonId",
                        column: x => x.FromResponsiblePersonId,
                        principalTable: "ResponsiblePeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceMovements_ResponsiblePeople_ToResponsiblePersonId",
                        column: x => x.ToResponsiblePersonId,
                        principalTable: "ResponsiblePeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceObservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastObservedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScanCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceObservations", x => x.Id);
                    table.CheckConstraint("CK_DeviceObservations_Range", "\"LastObservedAt\" >= \"ObservedAt\"");
                    table.CheckConstraint("CK_DeviceObservations_ScanCount", "\"ScanCount\" >= 1");
                    table.ForeignKey(
                        name: "FK_DeviceObservations_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceObservations_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_Device_AssignedAt",
                table: "Assignments",
                columns: new[] { "DeviceId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_ExpectedComputerId",
                table: "Assignments",
                column: "ExpectedComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_ResponsiblePersonId",
                table: "Assignments",
                column: "ResponsiblePersonId");

            migrationBuilder.CreateIndex(
                name: "UX_Assignments_Device_Active",
                table: "Assignments",
                column: "DeviceId",
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Computers_Hostname",
                table: "Computers",
                column: "Hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMovements_Device_ChangedAt",
                table: "DeviceMovements",
                columns: new[] { "DeviceId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMovements_FromComputerId",
                table: "DeviceMovements",
                column: "FromComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMovements_FromResponsiblePersonId",
                table: "DeviceMovements",
                column: "FromResponsiblePersonId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMovements_ToComputerId",
                table: "DeviceMovements",
                column: "ToComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMovements_ToResponsiblePersonId",
                table: "DeviceMovements",
                column: "ToResponsiblePersonId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceObservations_ComputerId_LastObservedAt",
                table: "DeviceObservations",
                columns: new[] { "ComputerId", "LastObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceObservations_Device_Last",
                table: "DeviceObservations",
                columns: new[] { "DeviceId", "LastObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_DeviceType",
                table: "Devices",
                column: "DeviceType");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_LastSeenComputerId",
                table: "Devices",
                column: "LastSeenComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_SerialNumber",
                table: "Devices",
                column: "SerialNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_VendorId_ProductId",
                table: "Devices",
                columns: new[] { "VendorId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "UX_Devices_Fingerprint",
                table: "Devices",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResponsiblePeople_Name",
                table: "ResponsiblePeople",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "DeviceMovements");

            migrationBuilder.DropTable(
                name: "DeviceObservations");

            migrationBuilder.DropTable(
                name: "ResponsiblePeople");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Computers");
        }
    }
}
