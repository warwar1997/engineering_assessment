CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;

ALTER DATABASE CHARACTER SET utf8mb4;

CREATE TABLE `businesses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_businesses` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `users` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `Email` varchar(320) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_users` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `instructors` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessId` int NOT NULL,
    `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_instructors` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_instructors_businesses_BusinessId` FOREIGN KEY (`BusinessId`) REFERENCES `businesses` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `packages` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessId` int NOT NULL,
    `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `Credits` int NOT NULL,
    `ValidityDays` int NOT NULL,
    `Price` decimal(10,2) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    CONSTRAINT `PK_packages` PRIMARY KEY (`Id`),
    CONSTRAINT `CK_packages_credits` CHECK (`Credits` > 0),
    CONSTRAINT `CK_packages_validity` CHECK (`ValidityDays` > 0),
    CONSTRAINT `FK_packages_businesses_BusinessId` FOREIGN KEY (`BusinessId`) REFERENCES `businesses` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `timetable_schedules` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessId` int NOT NULL,
    `InstructorId` int NOT NULL,
    `ClassName` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `StartTime` datetime(6) NOT NULL,
    `EndTime` datetime(6) NOT NULL,
    `Capacity` int NOT NULL,
    `BookedCount` int NOT NULL,
    CONSTRAINT `PK_timetable_schedules` PRIMARY KEY (`Id`),
    CONSTRAINT `CK_schedules_booked` CHECK (`BookedCount` >= 0 AND `BookedCount` <= `Capacity`),
    CONSTRAINT `CK_schedules_capacity` CHECK (`Capacity` > 0),
    CONSTRAINT `CK_schedules_time` CHECK (`EndTime` > `StartTime`),
    CONSTRAINT `FK_timetable_schedules_businesses_BusinessId` FOREIGN KEY (`BusinessId`) REFERENCES `businesses` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_timetable_schedules_instructors_InstructorId` FOREIGN KEY (`InstructorId`) REFERENCES `instructors` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `user_packages` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `PackageId` int NOT NULL,
    `BusinessId` int NOT NULL,
    `TotalCredits` int NOT NULL,
    `RemainingCredits` int NOT NULL,
    `ReservedCredits` int NOT NULL,
    `PurchasedAt` datetime(6) NOT NULL,
    `ExpiresAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_user_packages` PRIMARY KEY (`Id`),
    CONSTRAINT `CK_user_packages_remaining` CHECK (`RemainingCredits` >= 0 AND `RemainingCredits` <= `TotalCredits`),
    CONSTRAINT `CK_user_packages_reserved` CHECK (`ReservedCredits` >= 0 AND `ReservedCredits` <= `RemainingCredits`),
    CONSTRAINT `FK_user_packages_businesses_BusinessId` FOREIGN KEY (`BusinessId`) REFERENCES `businesses` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_user_packages_packages_PackageId` FOREIGN KEY (`PackageId`) REFERENCES `packages` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_user_packages_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `bookings` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `ScheduleId` int NOT NULL,
    `UserPackageId` int NOT NULL,
    `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `Source` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `BookedAt` datetime(6) NOT NULL,
    `CancelledAt` datetime(6) NULL,
    `CreditRefunded` tinyint(1) NOT NULL,
    CONSTRAINT `PK_bookings` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_bookings_timetable_schedules_ScheduleId` FOREIGN KEY (`ScheduleId`) REFERENCES `timetable_schedules` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_bookings_user_packages_UserPackageId` FOREIGN KEY (`UserPackageId`) REFERENCES `user_packages` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_bookings_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `waitlist_entries` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `ScheduleId` int NOT NULL,
    `UserPackageId` int NOT NULL,
    `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `JoinedAt` datetime(6) NOT NULL,
    `ResolvedAt` datetime(6) NULL,
    `BookingId` int NULL,
    `ResolutionNote` varchar(500) CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_waitlist_entries` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_waitlist_entries_bookings_BookingId` FOREIGN KEY (`BookingId`) REFERENCES `bookings` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_waitlist_entries_timetable_schedules_ScheduleId` FOREIGN KEY (`ScheduleId`) REFERENCES `timetable_schedules` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_waitlist_entries_user_packages_UserPackageId` FOREIGN KEY (`UserPackageId`) REFERENCES `user_packages` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_waitlist_entries_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `credit_transactions` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UserPackageId` int NOT NULL,
    `Type` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
    `RemainingDelta` int NOT NULL,
    `ReservedDelta` int NOT NULL,
    `BookingId` int NULL,
    `WaitlistEntryId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_credit_transactions` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_credit_transactions_bookings_BookingId` FOREIGN KEY (`BookingId`) REFERENCES `bookings` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_credit_transactions_user_packages_UserPackageId` FOREIGN KEY (`UserPackageId`) REFERENCES `user_packages` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_credit_transactions_waitlist_entries_WaitlistEntryId` FOREIGN KEY (`WaitlistEntryId`) REFERENCES `waitlist_entries` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_bookings_ScheduleId_Status` ON `bookings` (`ScheduleId`, `Status`);

CREATE INDEX `IX_bookings_UserId_Status` ON `bookings` (`UserId`, `Status`);

CREATE INDEX `IX_bookings_UserPackageId` ON `bookings` (`UserPackageId`);

CREATE UNIQUE INDEX `IX_businesses_Name` ON `businesses` (`Name`);

CREATE INDEX `IX_credit_transactions_BookingId` ON `credit_transactions` (`BookingId`);

CREATE INDEX `IX_credit_transactions_UserPackageId_CreatedAt` ON `credit_transactions` (`UserPackageId`, `CreatedAt`);

CREATE INDEX `IX_credit_transactions_WaitlistEntryId` ON `credit_transactions` (`WaitlistEntryId`);

CREATE INDEX `IX_instructors_BusinessId` ON `instructors` (`BusinessId`);

CREATE INDEX `IX_packages_BusinessId` ON `packages` (`BusinessId`);

CREATE INDEX `IX_timetable_schedules_BusinessId_StartTime` ON `timetable_schedules` (`BusinessId`, `StartTime`);

CREATE INDEX `IX_timetable_schedules_InstructorId` ON `timetable_schedules` (`InstructorId`);

CREATE INDEX `IX_timetable_schedules_StartTime` ON `timetable_schedules` (`StartTime`);

CREATE INDEX `IX_user_packages_BusinessId` ON `user_packages` (`BusinessId`);

CREATE INDEX `IX_user_packages_PackageId` ON `user_packages` (`PackageId`);

CREATE INDEX `IX_user_packages_UserId_BusinessId_ExpiresAt` ON `user_packages` (`UserId`, `BusinessId`, `ExpiresAt`);

CREATE UNIQUE INDEX `IX_users_Email` ON `users` (`Email`);

CREATE INDEX `IX_waitlist_entries_BookingId` ON `waitlist_entries` (`BookingId`);

CREATE INDEX `IX_waitlist_entries_ScheduleId_Status_JoinedAt_Id` ON `waitlist_entries` (`ScheduleId`, `Status`, `JoinedAt`, `Id`);

CREATE INDEX `IX_waitlist_entries_UserId_Status` ON `waitlist_entries` (`UserId`, `Status`);

CREATE INDEX `IX_waitlist_entries_UserPackageId` ON `waitlist_entries` (`UserPackageId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261120163139_InitialCreate', '8.0.11');

COMMIT;

