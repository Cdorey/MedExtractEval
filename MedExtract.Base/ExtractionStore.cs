using MedExtractEval.Data;
using MedExtractEval.Shared.Model;
using Microsoft.EntityFrameworkCore;

namespace MedExtract.Base
{
    public sealed class ExtractionStore
    {
        private static ConnectionStringResolver? _resolver;

        private static readonly Lazy<DbContextFactory> _factory = new(CreateFactory, LazyThreadSafetyMode.ExecutionAndPublication);

        public ExtractionStore(ConnectionStringResolver connectionStringResolver) => InitializeResolver(connectionStringResolver);


        public async Task<List<Experiment>> ListExperimentsAsync(CancellationToken ct = default)
        {
            await using var db = CreateDb();
            return await db.Experiments.AsNoTracking().ToListAsync(ct);
        }

        public async Task<List<CaseItem>> ListCasesAsync(CancellationToken ct = default)
        {
            await using var db = CreateDb();
            return await db.Cases.AsNoTracking().ToListAsync(ct);
        }

        public async Task<List<ModelConfig>> ListModelConfigsAsync(CancellationToken ct = default)
        {
            await using var db = CreateDb();
            return await db.ModelConfigs.AsNoTracking().ToListAsync(ct);
        }

        public async Task<List<CaseItem>> ListPendingCaseItemsAsync(Experiment experiment, ModelConfig modelConfig, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(experiment);
            ArgumentNullException.ThrowIfNull(modelConfig);

            var caseIds = experiment.IncludedCaseIds;
            if (caseIds is null || caseIds.Length == 0)
                return [];

            await using var db = CreateDb();

            // 找出 experiment 内的 caseIds 中，尚不存在对应 (experimentId, modelConfigId, caseId) 的 ModelExtraction
            var pending = await db.Cases
                .AsNoTracking()
                .Where(c => caseIds.Contains(c.Id))
                .Where(c => !db.ModelExtractions.Any(mx =>
                    mx.ExperimentId == experiment.Id &&
                    mx.ModelConfigId == modelConfig.Id &&
                    mx.CaseId == c.Id))
                .OrderBy(c => c.Id)
                .ToListAsync(ct);

            return pending;
        }

        public async Task<Experiment> CreateAndAddExperimentAsync(string name, Guid[] cases, string description, CancellationToken ct = default)
        {
            await using var db = CreateDb();

            var experiment = new Experiment
            {
                Name = name,
                IncludedCaseIds = cases,
                CreatedAt = DateTime.UtcNow,
                Description = description
            };

            await db.Experiments.AddAsync(experiment, ct);
            await db.SaveChangesAsync(ct);
            return experiment;
        }

        public async Task AddModelExtractionAsync(ModelExtraction extraction, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(extraction);
            // 关键：不要让导航对象跟着进来触发连带插入
            extraction.Experiment = null;
            extraction.Case = null;
            extraction.ModelConfig = null;

            await using var db = CreateDb();

            await db.ModelExtractions.AddAsync(extraction, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task<ModelConfig> CreateOrGetModelConfigAsync(string modelName, string provider, string versionTag, string promptTemplate, double temperature, double topP, bool isDeterministic, CancellationToken ct = default)
        {
            await using var db = CreateDb();

            // 1) 先查（NoTracking，避免跟踪开销）
            var existing = await db.ModelConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.ModelName == modelName &&
                    x.Provider == provider &&
                    x.VersionTag == versionTag &&
                    x.PromptTemplate == promptTemplate &&
                    x.Temperature == temperature &&
                    x.TopP == topP &&
                    x.IsDeterministic == isDeterministic, ct);

            if (existing is not null)
                return existing;

            // 2) 再建
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

            await db.ModelConfigs.AddAsync(config, ct);

            try
            {
                await db.SaveChangesAsync(ct);
                return config;
            }
            catch (DbUpdateException)
            {
                // 3) 并发兜底：如果别的线程刚好插入了同样的记录，这里会因唯一约束失败
                //    此时再查一次返回“赢家”
                var winner = await db.ModelConfigs
                    .AsNoTracking()
                    .FirstAsync(x =>
                        x.ModelName == modelName &&
                        x.Provider == provider &&
                        x.VersionTag == versionTag &&
                        x.PromptTemplate == promptTemplate &&
                        x.Temperature == temperature &&
                        x.TopP == topP &&
                        x.IsDeterministic == isDeterministic, ct);

                return winner;
            }
        }
        
        // （可选）批量写入：显著减少 SaveChanges 次数
        public async Task AddModelExtractionsAsync(IReadOnlyCollection<ModelExtraction> extractions, CancellationToken ct = default)
        {
            if (extractions is null || extractions.Count == 0) return;
            await using var db = CreateDb();

            await db.ModelExtractions.AddRangeAsync(extractions, ct);
            await db.SaveChangesAsync(ct);
        }

        private static MedEvalDbContext CreateDb()
        {
            var db = _factory.Value.Create();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            return db;
        }

        private static void InitializeResolver(ConnectionStringResolver resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);

            var existing = Interlocked.CompareExchange(ref _resolver, resolver, null);
            if (existing is null) return;

            if (!ReferenceEquals(existing, resolver))
                throw new InvalidOperationException("ConnectionStringResolver was initialized with a different delegate.");
        }

        private static DbContextFactory CreateFactory()
        {
            var resolver = Volatile.Read(ref _resolver) ?? throw new InvalidOperationException("ConnectionStringResolver is not initialized.");
            void configure(DbContextOptionsBuilder<MedEvalDbContext> ob, string cs) => ob.UseSqlServer(cs);
            MedEvalDbContext activator(DbContextOptions<MedEvalDbContext> options) => new(options);

            return new DbContextFactory(resolver, configure, activator);
        }
    }
}
