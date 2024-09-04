using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TrueVote.Api.Models;

namespace TrueVote.Api.Interfaces
{
    public interface ITrueVoteDbContext
    {
        DbSet<UserModel> Users { get; set; }
        DbSet<ElectionModel> Elections { get; set; }
        DbSet<RaceModel> Races { get; set; }
        DbSet<CandidateModel> Candidates { get; set; }
        DbSet<BallotModel> Ballots { get; set; }
        DbSet<TimestampModel> Timestamps { get; set; }
        DbSet<BallotHashModel> BallotHashes { get; set; }
        DbSet<FeedbackModel> Feedbacks { get; set; }
        DbSet<AccessCodeModel> ElectionAccessCodes { get; set; }
        DbSet<UsedAccessCodeModel> UsedAccessCodes { get; set; }
        DbSet<ElectionUserBindingModel> ElectionUserBindings { get; set; }

        Task<bool> EnsureCreatedAsync();
        Task<int> SaveChangesAsync();
        ValueTask<EntityEntry<TEntity>> AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : class;
    }
}
