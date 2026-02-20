using MedExtractEval.Data;
using MedExtractEval.Shared.Model;
using Microsoft.EntityFrameworkCore;

namespace MedExtract.Base
{
    public sealed class ExtractionStore : IAsyncDisposable
    {
        private static ConnectionStringResolver? _resolver;
        private static readonly Lazy<DbContextFactory> _factory = new(CreateFactory, LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly MedEvalDbContext _db;
        private bool _disposed;

        public ExtractionStore(ConnectionStringResolver connectionStringResolver)
        {
            InitializeResolver(connectionStringResolver);

            _db = _factory.Value.Create();

            // 可选：你大量使用 AsNoTracking 的话，可以全局设置一次
            _db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        public IQueryable<Experiment> Experiments
        {
            get { ThrowIfDisposed(); return _db.Experiments; }
        }

        public IQueryable<CaseItem> Cases
        {
            get { ThrowIfDisposed(); return _db.Cases; }
        }

        public IQueryable<ModelConfig> ModelConfigs
        {
            get { ThrowIfDisposed(); return _db.ModelConfigs; }
        }

        public async Task<Experiment> CreateAndAddExperimentAsync(string name, Guid[] cases, string description, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var experiment = new Experiment
            {
                Name = name,
                IncludedCaseIds = cases,
                CreatedAt = DateTime.UtcNow,
                Description = description
            };

            await _db.Experiments.AddAsync(experiment, ct);
            await _db.SaveChangesAsync(ct);
            return experiment;
        }

        public async Task AddModelExtractionAsync(ModelExtraction extraction, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(extraction);

            await _db.ModelExtractions.AddAsync(extraction, ct);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<ModelConfig> CreateAndAddModelConfigAsync(string modelName, string provider, string versionTag, string promptTemplate, double temperature, double topP, bool isDeterministic, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var config = new ModelConfig
            {
                Id = Guid.NewGuid(),
                ModelName = modelName,
                Provider = provider,
                VersionTag = versionTag,
                PromptTemplate = promptTemplate,
                Temperature = temperature,
                TopP = topP,
                IsDeterministic = isDeterministic
            };

            await _db.ModelConfigs.AddAsync(config, ct);
            await _db.SaveChangesAsync(ct);
            return config;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await _db.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        private static void InitializeResolver(ConnectionStringResolver resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);

            ConnectionStringResolver? existing = Interlocked.CompareExchange(ref _resolver, resolver, null);
            if (existing is null) return;

            // 可选：如果你希望“只能初始化一次且必须完全一致”，可以直接抛
            // throw new InvalidOperationException("ConnectionStringResolver has already been initialized.");

            // 或者：允许重复调用但要求同一个实例（更利于多处构造但配置一致）
            if (!ReferenceEquals(existing, resolver))
                throw new InvalidOperationException("ConnectionStringResolver was initialized with a different delegate.");
        }

        private static DbContextFactory CreateFactory()
        {
            ConnectionStringResolver? resolver = Volatile.Read(ref _resolver) ?? throw new InvalidOperationException("ConnectionStringResolver is not initialized.");
            void configure(DbContextOptionsBuilder<MedEvalDbContext> ob, string cs)
            {
                ob.UseSqlServer(cs);
            }

            MedEvalDbContext activator(DbContextOptions<MedEvalDbContext> options)
            {
                return new(options);
            }

            return new DbContextFactory(resolver, configure, activator);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ExtractionStore));
        }
    }
}
