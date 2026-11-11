using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Data.Configuration
{
    public static class GoogleCalendarModelConfiguration
    {
        public static void ApplyGoogleCalendar(this ModelBuilder builder)
        {
            builder.Entity<GoogleCalendarConnection>(entity =>
            {
                entity.HasKey(item => item.UserId);
                entity.Property(item => item.AccessToken).HasMaxLength(8192);
                entity.Property(item => item.RefreshToken).HasMaxLength(8192);
                entity.Property(item => item.TaskListId).HasMaxLength(1024);
                entity.Property(item => item.LastError).HasMaxLength(500);
            });
            builder.Entity<GoogleCalendarAuthorization>(entity =>
            {
                entity.HasKey(item => item.StateHash);
                entity.Property(item => item.StateHash).HasMaxLength(64);
                entity.Property(item => item.CodeVerifier).HasMaxLength(1024);
                entity.Property(item => item.ReturnPath).HasMaxLength(30);
                entity.HasIndex(item => new { item.UserId, item.ExpiresAt });
            });
            builder.Entity<GoogleSyncItem>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Property(item => item.SourceKey).HasMaxLength(200);
                entity.Property(item => item.ResourceKind).HasMaxLength(10);
                entity.Property(item => item.RemoteId).HasMaxLength(1024);
                entity.Property(item => item.DesiredHash).HasMaxLength(64);
                entity.Property(item => item.LastError).HasMaxLength(500);
                entity.HasIndex(item => new { item.UserId, item.SourceKey }).IsUnique();
                entity.HasIndex(item => new { item.UserId, item.NeedsSync, item.RetryAt });
            });
        }
    }
}
