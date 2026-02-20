using MedExtractEval.Data;
using Microsoft.EntityFrameworkCore;

namespace MedExtract.Base
{
    public delegate string ConnectionStringResolver();

    internal sealed class DbContextFactory
    {
        private readonly DbContextOptions<MedEvalDbContext> _options;
        private readonly Func<DbContextOptions<MedEvalDbContext>, MedEvalDbContext> _activator;

        public DbContextFactory(ConnectionStringResolver resolveConnectionString, Action<DbContextOptionsBuilder<MedEvalDbContext>, string> configure, Func<DbContextOptions<MedEvalDbContext>, MedEvalDbContext> activator)
        {
            ArgumentNullException.ThrowIfNull(resolveConnectionString);
            ArgumentNullException.ThrowIfNull(configure);
            _activator = activator ?? throw new ArgumentNullException(nameof(activator));

            var cs = resolveConnectionString();
            if (string.IsNullOrWhiteSpace(cs))
                throw new ArgumentException("Connection string is empty.", nameof(resolveConnectionString));

            var ob = new DbContextOptionsBuilder<MedEvalDbContext>();
            configure(ob, cs);

            _options = ob.Options;
        }

        public MedEvalDbContext Create()
        {
            return _activator(_options);
        }

        public async Task<TResult> WithContextAsync<TResult>(Func<MedEvalDbContext, Task<TResult>> action, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            await using MedEvalDbContext db = Create();
            ct.ThrowIfCancellationRequested();
            return await action(db);
        }

        public async Task WithContextAsync(Func<MedEvalDbContext, Task> action, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            await using MedEvalDbContext db = Create();
            ct.ThrowIfCancellationRequested();
            await action(db);
        }
    }
}
