using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using Moq;

namespace ChequeAR.Tests
{
    internal static class TestHelpers
    {
        /// <summary>
        /// AuditDispatcher with a far-future flush interval so the background timer
        /// never races with assertions during a unit test.
        /// </summary>
        public static AuditDispatcher NoopAuditDispatcher()
            => new AuditDispatcher(Mock.Of<IAuditRepository>(), flushIntervalMs: 600000);

        public static ValidationResult ValidOk()
            => new ValidationResult { IsValid = true, Errors = new string[0], Warnings = new string[0] };

        public static ValidationResult ValidFail(params string[] errors)
            => new ValidationResult { IsValid = false, Errors = errors, Warnings = new string[0] };
    }
}
