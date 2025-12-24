# 🏗️ Guide de refonte du projet API_BioR from scratch

## 🎯 Vue d'ensemble de la refonte

Si vous deviez **reconstruire ce projet de zéro**, voici le plan **optimal par étapes** pour éviter les pièges et construire une architecture solide dès le départ.

---

## 📋 Phase 0 : Préparation & Design (1-2 jours)

### ✅ Checklist avant de commencer

#### 1. **Clarifier les exigences métier**

```markdown
Questions essentielles à poser :

📌 Synchronisation
- Quels endpoints Dynamics 365 doivent être synchronisés ?
- Quelle fréquence de synchronisation ? (temps réel / batch)
- Quel volume de données attendu ? (10k, 100k, 1M records ?)
- Faut-il gérer des suppressions logiques ou physiques ?

📌 Confirmations
- Quels types de commandes doivent être confirmées ?
- Quel est le SLA de confirmation ? (temps réel / différé)
- Y a-t-il des règles métier spécifiques ? (statuts, validations)

📌 Intégrations externes
- Quels systèmes doivent être intégrés ? (SpeedWMS, REE, autres ?)
- Quels formats de données ? (JSON, XML, CSV ?)
- Y a-t-il des API tierces à appeler ?

📌 Performance
- Combien de temps max pour une sync complète ?
- Combien de requêtes simultanées ?
- Faut-il gérer des retries / circuit breakers ?
```

#### 2. **Auditer l'architecture existante**

**✅ Points forts de l'architecture actuelle :**
- Injection de dépendances bien structurée
- Services découplés et responsabilité unique
- Traçabilité complète (JSON_OUT)
- Gestion d'erreurs robuste
- Confirmations automatiques

**⚠️ Points à améliorer :**
- Architecture monolithique (tout dans Program.cs)
- Pas de tests unitaires
- Configuration mixte (certains hardcodés)
- Pas de gestion de queues pour les traitements lourds
- Logs basiques (pas de structured logging)
- Pas de monitoring/alerting
- Pas de versioning API
- Gestion de secrets en clair (appsettings.json)

#### 3. **Choisir l'architecture cible**

**🎯 Architecture recommandée : Clean Architecture + CQRS simplifié**

```
┌─────────────────────────────────────────────────────────┐
│                    API_BioR.Console                     │
│              (Point d'entrée orchestrateur)             │
└──────────────────────┬──────────────────────────────────┘
                       │
       ┌───────────────┼───────────────┐
       │               │               │
       ▼               ▼               ▼
┌──────────┐    ┌──────────┐    ┌──────────┐
│Application│    │Domain    │    │Infrastructure│
│  Layer   │───▶│  Layer   │◀───│   Layer     │
└──────────┘    └──────────┘    └──────────┘
│Commands/     │Entities      │Repositories
│Queries       │Interfaces    │Services
│Handlers      │Exceptions    │External APIs
```

---

## 🏗️ Phase 1 : Setup du projet (2-3 heures)

### Étape 1.1 : Créer la structure de solution

```powershell
# Créer le dossier racine
mkdir API_BioR_v2
cd API_BioR_v2

# Créer la solution
dotnet new sln -n API_BioR

# Créer les projets par couche
dotnet new console -n API_BioR.Console -f net8.0
dotnet new classlib -n API_BioR.Domain -f net8.0
dotnet new classlib -n API_BioR.Application -f net8.0
dotnet new classlib -n API_BioR.Infrastructure -f net8.0
dotnet new xunit -n API_BioR.Tests -f net8.0

# Ajouter les projets à la solution
dotnet sln add API_BioR.Console/API_BioR.Console.csproj
dotnet sln add API_BioR.Domain/API_BioR.Domain.csproj
dotnet sln add API_BioR.Application/API_BioR.Application.csproj
dotnet sln add API_BioR.Infrastructure/API_BioR.Infrastructure.csproj
dotnet sln add API_BioR.Tests/API_BioR.Tests.csproj

# Définir les références entre projets
cd API_BioR.Console
dotnet add reference ../API_BioR.Application/API_BioR.Application.csproj
dotnet add reference ../API_BioR.Infrastructure/API_BioR.Infrastructure.csproj

cd ../API_BioR.Application
dotnet add reference ../API_BioR.Domain/API_BioR.Domain.csproj

cd ../API_BioR.Infrastructure
dotnet add reference ../API_BioR.Domain/API_BioR.Domain.csproj
dotnet add reference ../API_BioR.Application/API_BioR.Application.csproj

cd ../API_BioR.Tests
dotnet add reference ../API_BioR.Domain/API_BioR.Domain.csproj
dotnet add reference ../API_BioR.Application/API_BioR.Application.csproj
dotnet add reference ../API_BioR.Infrastructure/API_BioR.Infrastructure.csproj

cd ..
```

**Structure finale :**

```
API_BioR_v2/
├── API_BioR.sln
│
├── src/
│   ├── API_BioR.Console/              # Point d'entrée
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   │
│   ├── API_BioR.Domain/               # Couche domaine (business logic pure)
│   │   ├── Entities/
│   │   │   ├── Article.cs
│   │   │   ├── PurchaseOrder.cs
│   │   │   ├── ReturnOrder.cs
│   │   │   ├── TransferOrder.cs
│   │   │   └── SalesOrder.cs
│   │   ├── ValueObjects/
│   │   │   ├── OrderStatus.cs
│   │   │   └── ConfirmationStatus.cs
│   │   ├── Interfaces/
│   │   │   ├── IRepository.cs
│   │   │   ├── IUnitOfWork.cs
│   │   │   └── IDynamicsApiClient.cs
│   │   └── Exceptions/
│   │       ├── SyncException.cs
│   │       └── ConfirmationException.cs
│   │
│   ├── API_BioR.Application/          # Couche application (use cases)
│   │   ├── Commands/
│   │   │   ├── SyncArticles/
│   │   │   │   ├── SyncArticlesCommand.cs
│   │   │   │   └── SyncArticlesHandler.cs
│   │   │   ├── ConfirmPurchaseOrder/
│   │   │   │   ├── ConfirmPurchaseOrderCommand.cs
│   │   │   │   └── ConfirmPurchaseOrderHandler.cs
│   │   │   └── ...
│   │   ├── Queries/
│   │   │   ├── GetPendingOrders/
│   │   │   │   ├── GetPendingOrdersQuery.cs
│   │   │   │   └── GetPendingOrdersHandler.cs
│   │   │   └── ...
│   │   ├── DTOs/
│   │   │   ├── ArticleDto.cs
│   │   │   ├── OrderDto.cs
│   │   │   └── SyncResultDto.cs
│   │   ├── Mappings/
│   │   │   └── AutoMapperProfile.cs
│   │   └── Interfaces/
│   │       └── ICommandHandler.cs
│   │
│   └── API_BioR.Infrastructure/       # Couche infrastructure (implémentation)
│       ├── Persistence/
│       │   ├── Repositories/
│       │   │   ├── ArticleRepository.cs
│       │   │   ├── OrderRepository.cs
│       │   │   └── JsonOutRepository.cs
│       │   ├── DbContext/
│       │   │   └── MiddlewareDbContext.cs
│       │   └── Migrations/
│       │
│       ├── ExternalServices/
│       │   ├── Dynamics/
│       │   │   ├── DynamicsApiClient.cs
│       │   │   ├── DynamicsAuthService.cs
│       │   │   └── Models/
│       │   ├── SpeedWms/
│       │   │   ├── SpeedWmsClient.cs
│       │   │   └── Models/
│       │   └── Translator/
│       │       └── TranslatorLauncher.cs
│       │
│       ├── Logging/
│       │   └── StructuredLogger.cs
│       │
│       └── Configuration/
│           ├── DependencyInjection.cs
│           └── Settings/
│               ├── DynamicsSettings.cs
│               └── DatabaseSettings.cs
│
└── tests/
    └── API_BioR.Tests/
        ├── Unit/
        │   ├── Domain/
        │   ├── Application/
        │   └── Infrastructure/
        └── Integration/
            ├── DynamicsApiTests.cs
            └── DatabaseTests.cs
```

### Étape 1.2 : Installer les packages essentiels

```xml
<!-- API_BioR.Console/API_BioR.Console.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
  <PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
  <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
  <PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
</ItemGroup>

<!-- API_BioR.Application/API_BioR.Application.csproj -->
<ItemGroup>
  <PackageReference Include="MediatR" Version="12.2.0" />
  <PackageReference Include="AutoMapper" Version="12.0.1" />
  <PackageReference Include="FluentValidation" Version="11.9.0" />
  <PackageReference Include="Polly" Version="8.2.0" />
</ItemGroup>

<!-- API_BioR.Infrastructure/API_BioR.Infrastructure.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.0" />
  <PackageReference Include="Dapper" Version="2.1.28" />
  <PackageReference Include="Microsoft.Identity.Client" Version="4.61.3" />
  <PackageReference Include="Refit" Version="7.0.0" />
  <PackageReference Include="Azure.Identity" Version="1.10.4" />
  <PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.5.0" />
</ItemGroup>

<!-- API_BioR.Tests/API_BioR.Tests.csproj -->
<ItemGroup>
  <PackageReference Include="xUnit" Version="2.6.4" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
  <PackageReference Include="Testcontainers" Version="3.7.0" />
</ItemGroup>
```

---

## 🧱 Phase 2 : Construire la couche Domain (1 jour)

### Étape 2.1 : Définir les entités métier

```csharp
// API_BioR.Domain/Entities/Article.cs
namespace API_BioR.Domain.Entities;

public class Article : BaseEntity
{
    public string ItemId { get; private set; }
    public string ItemName { get; private set; }
    public DateTime ModifiedDate { get; private set; }
    public string ItemType { get; private set; }
    public decimal QuantityOnHand { get; private set; }
    public ConfirmationStatus Status { get; private set; }

    private Article() { } // EF Core

    public static Article Create(string itemId, string itemName, DateTime modifiedDate)
    {
        // Validation métier
        if (string.IsNullOrWhiteSpace(itemId))
            throw new DomainException("ItemId cannot be empty");

        return new Article
        {
            ItemId = itemId,
            ItemName = itemName,
            ModifiedDate = modifiedDate,
            Status = ConfirmationStatus.Pending
        };
    }

    public void MarkAsConfirmed()
    {
        if (Status == ConfirmationStatus.Confirmed)
            throw new DomainException("Article already confirmed");

        Status = ConfirmationStatus.Confirmed;
        AddDomainEvent(new ArticleConfirmedEvent(ItemId));
    }
}

// API_BioR.Domain/Entities/BaseEntity.cs
public abstract class BaseEntity
{
    public int Id { get; protected set; }
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    
    private List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
```

### Étape 2.2 : Définir les interfaces (contrats)

```csharp
// API_BioR.Domain/Interfaces/IRepository.cs
namespace API_BioR.Domain.Interfaces;

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
}

// API_BioR.Domain/Interfaces/IArticleRepository.cs
public interface IArticleRepository : IRepository<Article>
{
    Task<Article?> GetByItemIdAsync(string itemId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Article>> GetPendingConfirmationsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Article>> GetModifiedAfterAsync(DateTime date, CancellationToken cancellationToken = default);
}

// API_BioR.Domain/Interfaces/IDynamicsApiClient.cs
public interface IDynamicsApiClient
{
    Task<IEnumerable<TDto>> GetDataAsync<TDto>(string endpoint, CancellationToken cancellationToken = default);
    Task<bool> ConfirmOrderAsync<TRequest>(string endpoint, TRequest request, CancellationToken cancellationToken = default);
    Task<TResponse> PostDataAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default);
}

// API_BioR.Domain/Interfaces/IUnitOfWork.cs
public interface IUnitOfWork : IDisposable
{
    IArticleRepository Articles { get; }
    IOrderRepository Orders { get; }
    IJsonOutRepository JsonOut { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
```

### Étape 2.3 : Définir les Value Objects

```csharp
// API_BioR.Domain/ValueObjects/ConfirmationStatus.cs
namespace API_BioR.Domain.ValueObjects;

public class ConfirmationStatus : ValueObject
{
    public string Value { get; private set; }

    public static readonly ConfirmationStatus Pending = new("Pending");
    public static readonly ConfirmationStatus Confirmed = new("Confirmed");
    public static readonly ConfirmationStatus Failed = new("Failed");

    private ConfirmationStatus(string value)
    {
        Value = value;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}

// API_BioR.Domain/ValueObjects/ValueObject.cs
public abstract class ValueObject
{
    protected abstract IEnumerable<object> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj == null || obj.GetType() != GetType())
            return false;

        var other = (ValueObject)obj;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
    }
}
```

---

## ⚙️ Phase 3 : Implémenter la couche Application (2-3 jours)

### Étape 3.1 : Setup MediatR pour CQRS

```csharp
// API_BioR.Application/Commands/SyncArticles/SyncArticlesCommand.cs
namespace API_BioR.Application.Commands.SyncArticles;

public record SyncArticlesCommand : IRequest<SyncResultDto>
{
    public DateTime? FromDate { get; init; }
}

// API_BioR.Application/Commands/SyncArticles/SyncArticlesHandler.cs
public class SyncArticlesHandler : IRequestHandler<SyncArticlesCommand, SyncResultDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDynamicsApiClient _dynamicsClient;
    private readonly ILogger<SyncArticlesHandler> _logger;

    public SyncArticlesHandler(
        IUnitOfWork unitOfWork,
        IDynamicsApiClient dynamicsClient,
        ILogger<SyncArticlesHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dynamicsClient = dynamicsClient;
        _logger = logger;
    }

    public async Task<SyncResultDto> Handle(SyncArticlesCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting article synchronization from {FromDate}", request.FromDate);

        var result = new SyncResultDto();

        try
        {
            // 1. Récupérer les articles de Dynamics 365
            var dynamicsArticles = await _dynamicsClient.GetDataAsync<ArticleDto>(
                "data/BRINT34ReleasedProducts",
                cancellationToken
            );

            // 2. Filtrer par date si nécessaire
            if (request.FromDate.HasValue)
            {
                dynamicsArticles = dynamicsArticles
                    .Where(a => a.ModifiedDate >= request.FromDate.Value)
                    .ToList();
            }

            // 3. Traiter chaque article
            foreach (var dto in dynamicsArticles)
            {
                var existingArticle = await _unitOfWork.Articles.GetByItemIdAsync(
                    dto.ItemId,
                    cancellationToken
                );

                if (existingArticle == null)
                {
                    // Nouvel article
                    var newArticle = Article.Create(dto.ItemId, dto.ItemName, dto.ModifiedDate);
                    await _unitOfWork.Articles.AddAsync(newArticle, cancellationToken);
                    result.NewRecords++;
                }
                else if (existingArticle.ModifiedDate < dto.ModifiedDate)
                {
                    // Article modifié
                    existingArticle.Update(dto.ItemName, dto.ModifiedDate);
                    await _unitOfWork.Articles.UpdateAsync(existingArticle, cancellationToken);
                    result.UpdatedRecords++;
                }
                else
                {
                    result.UnchangedRecords++;
                }
            }

            // 4. Sauvegarder les changements
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            result.Success = true;
            _logger.LogInformation(
                "Article synchronization completed: {NewRecords} new, {UpdatedRecords} updated",
                result.NewRecords,
                result.UpdatedRecords
            );

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during article synchronization");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }
}
```

### Étape 3.2 : Ajouter la validation avec FluentValidation

```csharp
// API_BioR.Application/Commands/SyncArticles/SyncArticlesValidator.cs
public class SyncArticlesValidator : AbstractValidator<SyncArticlesCommand>
{
    public SyncArticlesValidator()
    {
        RuleFor(x => x.FromDate)
            .LessThanOrEqualTo(DateTime.UtcNow)
            .When(x => x.FromDate.HasValue)
            .WithMessage("FromDate cannot be in the future");
    }
}

// API_BioR.Application/Behaviors/ValidationBehavior.cs
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken))
        );

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Any())
            throw new ValidationException(failures);

        return await next();
    }
}
```

---

## 🏭 Phase 4 : Implémenter la couche Infrastructure (3-4 jours)

### Étape 4.1 : Setup Entity Framework Core

```csharp
// API_BioR.Infrastructure/Persistence/DbContext/MiddlewareDbContext.cs
namespace API_BioR.Infrastructure.Persistence.DbContext;

public class MiddlewareDbContext : DbContext
{
    public DbSet<Article> Articles { get; set; }
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
    public DbSet<JsonOutEntry> JsonOutEntries { get; set; }

    public MiddlewareDbContext(DbContextOptions<MiddlewareDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiddlewareDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Dispatcher les domain events avant de sauvegarder
        await DispatchDomainEventsAsync();
        
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchDomainEventsAsync()
    {
        var domainEntities = ChangeTracker
            .Entries<BaseEntity>()
            .Where(x => x.Entity.DomainEvents.Any())
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(x => x.Entity.DomainEvents)
            .ToList();

        domainEntities.ForEach(entity => entity.Entity.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent);
        }
    }
}

// API_BioR.Infrastructure/Persistence/Configurations/ArticleConfiguration.cs
public class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder)
    {
        builder.ToTable("JSON_IN");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("JSON_KEYU");

        builder.Property(a => a.ItemId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.ItemName)
            .HasMaxLength(255);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("JSON_CRDA")
            .HasDefaultValueSql("GETDATE()");

        builder.HasIndex(a => a.ItemId).IsUnique();
        builder.HasIndex(a => a.ModifiedDate);
    }
}
```

### Étape 4.2 : Implémenter les repositories

```csharp
// API_BioR.Infrastructure/Persistence/Repositories/ArticleRepository.cs
public class ArticleRepository : IArticleRepository
{
    private readonly MiddlewareDbContext _context;

    public ArticleRepository(MiddlewareDbContext context)
    {
        _context = context;
    }

    public async Task<Article?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Articles
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Article?> GetByItemIdAsync(string itemId, CancellationToken cancellationToken = default)
    {
        return await _context.Articles
            .FirstOrDefaultAsync(a => a.ItemId == itemId, cancellationToken);
    }

    public async Task<IEnumerable<Article>> GetPendingConfirmationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Articles
            .Where(a => a.Status == ConfirmationStatus.Pending)
            .ToListAsync(cancellationToken);
    }

    public async Task<Article> AddAsync(Article entity, CancellationToken cancellationToken = default)
    {
        await _context.Articles.AddAsync(entity, cancellationToken);
        return entity;
    }

    // ... autres méthodes
}

// API_BioR.Infrastructure/Persistence/UnitOfWork.cs
public class UnitOfWork : IUnitOfWork
{
    private readonly MiddlewareDbContext _context;
    private IDbContextTransaction? _transaction;

    public IArticleRepository Articles { get; }
    public IOrderRepository Orders { get; }
    public IJsonOutRepository JsonOut { get; }

    public UnitOfWork(
        MiddlewareDbContext context,
        IArticleRepository articles,
        IOrderRepository orders,
        IJsonOutRepository jsonOut)
    {
        _context = context;
        Articles = articles;
        Orders = orders;
        JsonOut = jsonOut;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
```

### Étape 4.3 : Implémenter le client Dynamics avec Refit

```csharp
// API_BioR.Infrastructure/ExternalServices/Dynamics/IDynamicsApi.cs
[Headers("Authorization: Bearer")]
public interface IDynamicsApi
{
    [Get("/data/BRINT34ReleasedProducts")]
    Task<DynamicsResponse<ArticleDto>> GetArticlesAsync(
        [Query] string? $filter = null,
        [Query] string? $select = null,
        CancellationToken cancellationToken = default
    );

    [Post("/data/BRPackingSlipValidationInterfaces/Microsoft.Dynamics.DataEntities.PostPackingSlip")]
    Task<ConfirmationResponse> ConfirmPackingSlipAsync(
        [Body] ConfirmationRequest request,
        CancellationToken cancellationToken = default
    );
}

// API_BioR.Infrastructure/ExternalServices/Dynamics/DynamicsApiClient.cs
public class DynamicsApiClient : IDynamicsApiClient
{
    private readonly IDynamicsApi _api;
    private readonly DynamicsAuthService _authService;
    private readonly ILogger<DynamicsApiClient> _logger;

    public DynamicsApiClient(
        IDynamicsApi api,
        DynamicsAuthService authService,
        ILogger<DynamicsApiClient> logger)
    {
        _api = api;
        _authService = authService;
        _logger = logger;
    }

    public async Task<IEnumerable<TDto>> GetDataAsync<TDto>(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetAccessTokenAsync();
        
        // Refit injecte automatiquement le token via [Headers("Authorization: Bearer")]
        
        if (typeof(TDto) == typeof(ArticleDto))
        {
            var response = await _api.GetArticlesAsync(cancellationToken: cancellationToken);
            return (IEnumerable<TDto>)response.Value;
        }

        throw new NotImplementedException($"Endpoint {endpoint} not implemented");
    }
}

// API_BioR.Infrastructure/ExternalServices/Dynamics/DynamicsAuthService.cs
public class DynamicsAuthService
{
    private readonly IConfidentialClientApplication _app;
    private readonly DynamicsSettings _settings;
    private string? _cachedToken;
    private DateTime _tokenExpiration;

    public DynamicsAuthService(IOptions<DynamicsSettings> settings)
    {
        _settings = settings.Value;
        
        _app = ConfidentialClientApplicationBuilder
            .Create(_settings.ClientId)
            .WithClientSecret(_settings.ClientSecret)
            .WithAuthority(new Uri($"https://login.microsoftonline.com/{_settings.TenantId}"))
            .Build();
    }

    public async Task<string> GetAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiration)
            return _cachedToken;

        var scopes = new[] { $"{_settings.ResourceUrl}/.default" };
        
        var result = await _app.AcquireTokenForClient(scopes).ExecuteAsync();
        
        _cachedToken = result.AccessToken;
        _tokenExpiration = result.ExpiresOn.UtcDateTime.AddMinutes(-5); // Refresh 5 min avant expiration
        
        return _cachedToken;
    }
}
```

### Étape 4.4 : Ajouter la résilience avec Polly

```csharp
// API_BioR.Infrastructure/Configuration/ResiliencePolicies.cs
public static class ResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    // Log retry
                    var logger = context.GetLogger();
                    logger?.LogWarning(
                        "Retry {RetryAttempt} after {Delay}s due to: {Reason}",
                        retryAttempt,
                        timespan.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()
                    );
                }
            );
    }

    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, timespan) =>
                {
                    // Circuit opened
                },
                onReset: () =>
                {
                    // Circuit closed
                }
            );
    }
}

// Configuration dans DependencyInjection.cs
services.AddRefitClient<IDynamicsApi>()
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(settings.BaseUrl))
    .AddPolicyHandler(ResiliencePolicies.GetRetryPolicy())
    .AddPolicyHandler(ResiliencePolicies.GetCircuitBreakerPolicy());
```

---

## 🔐 Phase 5 : Sécurité et Configuration (1 jour)

### Étape 5.1 : Utiliser Azure Key Vault pour les secrets

```csharp
// API_BioR.Infrastructure/Configuration/KeyVaultConfiguration.cs
public static class KeyVaultConfiguration
{
    public static IConfigurationBuilder AddAzureKeyVault(
        this IConfigurationBuilder builder,
        string keyVaultUrl)
    {
        var credential = new DefaultAzureCredential();
        
        builder.AddAzureKeyVault(
            new Uri(keyVaultUrl),
            credential
        );

        return builder;
    }
}

// Dans Program.cs
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        if (context.HostingEnvironment.IsProduction())
        {
            var keyVaultUrl = config.Build()["KeyVault:Url"];
            config.AddAzureKeyVault(keyVaultUrl!);
        }
    });
```

### Étape 5.2 : Configuration par environnement

```json
// appsettings.json (valeurs par défaut)
{
  "KeyVault": {
    "Url": "https://your-keyvault.vault.azure.net/"
  },
  "Dynamics": {
    "BaseUrl": "https://br-uat.sandbox.operations.eu.dynamics.com/",
    "TenantId": "",  // Vide, récupéré depuis KeyVault
    "ClientId": "",
    "ClientSecret": ""
  },
  "Database": {
    "ConnectionString": ""  // Récupéré depuis KeyVault
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/api-bior-.log",
          "rollingInterval": "Day",
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}

// appsettings.Development.json
{
  "Dynamics": {
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "ClientSecret": "***REPLACE_WITH_YOUR_CLIENT_SECRET***"
  },
  "Database": {
    "ConnectionString": "Server=7.2.160.173;Database=Middleware;..."
  }
}
```

---

## 🧪 Phase 6 : Tests (2-3 jours)

### Étape 6.1 : Tests unitaires Domain

```csharp
// API_BioR.Tests/Unit/Domain/ArticleTests.cs
public class ArticleTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreateArticle()
    {
        // Arrange
        var itemId = "COSM001";
        var itemName = "Crème BioRécup";
        var modifiedDate = DateTime.UtcNow;

        // Act
        var article = Article.Create(itemId, itemName, modifiedDate);

        // Assert
        article.Should().NotBeNull();
        article.ItemId.Should().Be(itemId);
        article.ItemName.Should().Be(itemName);
        article.Status.Should().Be(ConfirmationStatus.Pending);
    }

    [Fact]
    public void Create_WithEmptyItemId_ShouldThrowException()
    {
        // Arrange & Act
        var act = () => Article.Create("", "Test", DateTime.UtcNow);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("ItemId cannot be empty");
    }

    [Fact]
    public void MarkAsConfirmed_WhenPending_ShouldConfirm()
    {
        // Arrange
        var article = Article.Create("COSM001", "Test", DateTime.UtcNow);

        // Act
        article.MarkAsConfirmed();

        // Assert
        article.Status.Should().Be(ConfirmationStatus.Confirmed);
        article.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ArticleConfirmedEvent>();
    }
}
```

### Étape 6.2 : Tests d'intégration avec Testcontainers

```csharp
// API_BioR.Tests/Integration/ArticleRepositoryTests.cs
public class ArticleRepositoryTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer;
    private MiddlewareDbContext _context;
    private IArticleRepository _repository;

    public ArticleRepositoryTests()
    {
        _sqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();

        var options = new DbContextOptionsBuilder<MiddlewareDbContext>()
            .UseSqlServer(_sqlContainer.GetConnectionString())
            .Options;

        _context = new MiddlewareDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        _repository = new ArticleRepository(_context);
    }

    [Fact]
    public async Task GetByItemIdAsync_ExistingArticle_ShouldReturnArticle()
    {
        // Arrange
        var article = Article.Create("COSM001", "Test Article", DateTime.UtcNow);
        await _repository.AddAsync(article);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByItemIdAsync("COSM001");

        // Assert
        result.Should().NotBeNull();
        result!.ItemId.Should().Be("COSM001");
    }

    public async Task DisposeAsync()
    {
        await _sqlContainer.DisposeAsync();
    }
}
```

---

## 🎯 Phase 7 : Monitoring et Observabilité (1-2 jours)

### Étape 7.1 : Structured Logging avec Serilog

```csharp
// Program.cs
var builder = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "API_BioR")
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            )
            .WriteTo.File(
                path: "logs/api-bior-.log",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            );

        // En production, ajouter Application Insights
        if (context.HostingEnvironment.IsProduction())
        {
            var instrumentationKey = context.Configuration["ApplicationInsights:InstrumentationKey"];
            configuration.WriteTo.ApplicationInsights(
                instrumentationKey,
                TelemetryConverter.Traces
            );
        }
    });
```

### Étape 7.2 : Métriques avec OpenTelemetry

```csharp
// Installer: OpenTelemetry.Extensions.Hosting
services.AddOpenTelemetry()
    .WithMetrics(builder =>
    {
        builder
            .AddRuntimeInstrumentation()
            .AddHttpClientInstrumentation()
            .AddAspNetCoreInstrumentation();
    })
    .WithTracing(builder =>
    {
        builder
            .AddSource("API_BioR")
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation();
    });

// Custom metrics
public class SyncMetrics
{
    private readonly Counter<int> _articlesSync;
    private readonly Histogram<double> _syncDuration;

    public SyncMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("API_BioR.Sync");
        
        _articlesSync = meter.CreateCounter<int>("sync.articles.count");
        _syncDuration = meter.CreateHistogram<double>("sync.duration");
    }

    public void RecordArticlesSync(int count)
    {
        _articlesSync.Add(count);
    }

    public void RecordSyncDuration(double durationMs)
    {
        _syncDuration.Record(durationMs);
    }
}
```

---

## 🚀 Phase 8 : CI/CD et Déploiement (1 jour)

### Étape 8.1 : Pipeline GitHub Actions

```yaml
# .github/workflows/ci-cd.yml
name: CI/CD Pipeline

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Run tests
        run: dotnet test --no-build --configuration Release --verbosity normal --collect:"XPlat Code Coverage"

      - name: Upload coverage to Codecov
        uses: codecov/codecov-action@v3

  publish:
    needs: build-and-test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Publish
        run: dotnet publish src/API_BioR.Console/API_BioR.Console.csproj -c Release -o publish

      - name: Upload artifact
        uses: actions/upload-artifact@v3
        with:
          name: api-bior-release
          path: publish/

  deploy:
    needs: publish
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest

    steps:
      - name: Download artifact
        uses: actions/download-artifact@v3
        with:
          name: api-bior-release

      - name: Deploy to Azure
        # Configurer selon votre cible (VM, Container, etc.)
        run: echo "Deploy to production"
```

---

## 📊 Résumé chronologique

| Phase | Durée estimée | Priorité | Valeur ajoutée |
|-------|---------------|----------|----------------|
| **Phase 0: Design** | 1-2 jours | 🔴 Critique | Évite la dette technique |
| **Phase 1: Setup projet** | 2-3h | 🔴 Critique | Structure solide |
| **Phase 2: Domain Layer** | 1 jour | 🔴 Critique | Business logic pure |
| **Phase 3: Application Layer** | 2-3 jours | 🔴 Critique | Use cases bien définis |
| **Phase 4: Infrastructure** | 3-4 jours | 🔴 Critique | Accès données robuste |
| **Phase 5: Sécurité** | 1 jour | 🟠 Important | Production-ready |
| **Phase 6: Tests** | 2-3 jours | 🟠 Important | Qualité assurée |
| **Phase 7: Monitoring** | 1-2 jours | 🟡 Recommandé | Observabilité |
| **Phase 8: CI/CD** | 1 jour | 🟡 Recommandé | Automatisation |

**Durée totale : 12-18 jours** (2-3 semaines) pour un développeur expérimenté

---

## ✅ Checklist de validation

### 🎯 Avant de coder

- [ ] Exigences métier clairement définies
- [ ] Architecture validée avec l'équipe
- [ ] Choix technologiques documentés
- [ ] Environnements préparés (Dev, UAT, Prod)

### 🏗️ Pendant le développement

- [ ] Tests unitaires écrits en même temps que le code
- [ ] Code reviews systématiques
- [ ] Documentation à jour (README, ADR)
- [ ] Logs structurés avec contexte

### 🚀 Avant la mise en production

- [ ] Tests d'intégration passent à 100%
- [ ] Tests de charge effectués
- [ ] Secrets migrés vers Key Vault
- [ ] Monitoring configuré (logs, métriques, alertes)
- [ ] Plan de rollback documenté
- [ ] Runbook opérationnel rédigé

---

## 🎓 Bonnes pratiques à suivre

### 1. **SOLID Principles**
- **S**ingle Responsibility : Chaque classe = 1 responsabilité
- **O**pen/Closed : Ouvert à l'extension, fermé à la modification
- **L**iskov Substitution : Les sous-types doivent être substituables
- **I**nterface Segregation : Interfaces spécifiques > interfaces générales
- **D**ependency Inversion : Dépendre d'abstractions, pas de concrétions

### 2. **Clean Architecture**
- Domain ne dépend de RIEN
- Application dépend de Domain uniquement
- Infrastructure dépend de Domain + Application
- Console dépend de tout mais contient le minimum

### 3. **Testing Strategy**
```
Tests unitaires       → 70% couverture (Domain + Application)
Tests d'intégration  → 20% couverture (Infrastructure)
Tests E2E            → 10% couverture (Scenarios critiques)
```

### 4. **Git Workflow**
```
main         → Production (toujours stable)
develop      → Intégration continue
feature/*    → Nouvelles fonctionnalités
bugfix/*     → Corrections de bugs
hotfix/*     → Corrections urgentes en production
```

---

## 🔮 Évolutions futures possibles

### Court terme (3-6 mois)
- [ ] API REST pour exposition des données
- [ ] Dashboard de monitoring temps réel
- [ ] Notifications (email/teams) sur erreurs
- [ ] Gestion des retries avec exponential backoff

### Moyen terme (6-12 mois)
- [ ] Migration vers Azure Functions (serverless)
- [ ] Event-driven architecture avec Azure Service Bus
- [ ] Cache distribué (Redis) pour performances
- [ ] API GraphQL pour flexibilité des requêtes

### Long terme (12+ mois)
- [ ] Microservices par domaine (Articles, Orders, etc.)
- [ ] CQRS complet avec Event Sourcing
- [ ] Scalabilité horizontale automatique
- [ ] Machine Learning pour prédictions

---

## 📚 Ressources recommandées

### Livres
- **Clean Architecture** - Robert C. Martin
- **Domain-Driven Design** - Eric Evans
- **Implementing Domain-Driven Design** - Vaughn Vernon
- **The Phoenix Project** - Gene Kim

### Cours en ligne
- **Pluralsight** : Clean Architecture, CQRS
- **Udemy** : .NET Microservices Architecture
- **Microsoft Learn** : Azure, .NET 8

### Communautés
- **GitHub** : Exemples Clean Architecture .NET
- **Stack Overflow** : Questions techniques
- **Reddit** : r/dotnet, r/csharp

---

**🎉 Vous êtes maintenant prêt à refaire le projet avec une architecture solide !**
