---
layout: default
title: Custom Providers
---

# Custom Providers

Extend Athena.Cache with custom cache providers, serialization methods, and key generation strategies to meet specific requirements.

## Custom Cache Providers

Create custom cache implementations for specialized storage backends.

### Base Cache Provider Interface

```csharp
public interface ICacheProvider
{
    Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetKeysAsync(string pattern = "*", CancellationToken cancellationToken = default);
}

public abstract class BaseCacheProvider : ICacheProvider
{
    protected readonly ICacheSerializer _serializer;
    protected readonly ILogger _logger;
    protected readonly CacheProviderOptions _options;

    protected BaseCacheProvider(
        ICacheSerializer serializer,
        ILogger logger,
        CacheProviderOptions options)
    {
        _serializer = serializer;
        _logger = logger;
        _options = options;
    }

    public abstract Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    public abstract Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    public abstract Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    public abstract Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    public abstract Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    public abstract Task ClearAsync(CancellationToken cancellationToken = default);
    public abstract Task<IEnumerable<string>> GetKeysAsync(string pattern = "*", CancellationToken cancellationToken = default);

    protected virtual string NormalizeKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Cache key cannot be null or empty", nameof(key));

        return _options.KeyPrefix + key;
    }

    protected virtual void LogOperation(string operation, string key, bool success, TimeSpan duration)
    {
        if (_options.EnableLogging)
        {
            _logger.LogDebug("Cache {Operation} for key {Key}: {Success} in {Duration}ms",
                operation, key, success ? "Success" : "Failed", duration.TotalMilliseconds);
        }
    }
}
```

### File System Cache Provider

```csharp
public class FileSystemCacheProvider : BaseCacheProvider
{
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _semaphore;

    public FileSystemCacheProvider(
        ICacheSerializer serializer,
        ILogger<FileSystemCacheProvider> logger,
        FileSystemCacheOptions options)
        : base(serializer, logger, options)
    {
        _cacheDirectory = options.CacheDirectory ?? Path.Combine(Path.GetTempPath(), "AthenaCache");
        _semaphore = new SemaphoreSlim(options.MaxConcurrentOperations, options.MaxConcurrentOperations);

        EnsureDirectoryExists();
    }

    public override async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedKey = NormalizeKey(key);
        var filePath = GetFilePath(normalizedKey);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                LogOperation("GET", key, false, stopwatch.Elapsed);
                return default(T);
            }

            var cacheItem = await ReadCacheItemAsync<T>(filePath, cancellationToken);
            
            if (cacheItem.IsExpired)
            {
                // Clean up expired item
                File.Delete(filePath);
                LogOperation("GET", key, false, stopwatch.Elapsed);
                return default(T);
            }

            LogOperation("GET", key, true, stopwatch.Elapsed);
            return cacheItem.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache item with key {Key}", key);
            LogOperation("GET", key, false, stopwatch.Elapsed);
            return default(T);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public override async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedKey = NormalizeKey(key);
        var filePath = GetFilePath(normalizedKey);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var cacheItem = new FileCacheItem<T>
            {
                Value = value,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiration.HasValue ? DateTimeOffset.UtcNow.Add(expiration.Value) : null
            };

            await WriteCacheItemAsync(filePath, cacheItem, cancellationToken);
            LogOperation("SET", key, true, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache item with key {Key}", key);
            LogOperation("SET", key, false, stopwatch.Elapsed);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public override async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedKey = NormalizeKey(key);
        var filePath = GetFilePath(normalizedKey);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                LogOperation("REMOVE", key, true, stopwatch.Elapsed);
            }
            else
            {
                LogOperation("REMOVE", key, false, stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache item with key {Key}", key);
            LogOperation("REMOVE", key, false, stopwatch.Elapsed);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public override async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var keys = await GetKeysAsync(pattern, cancellationToken);
        var tasks = keys.Select(key => RemoveAsync(key, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public override async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        var filePath = GetFilePath(normalizedKey);

        if (!File.Exists(filePath)) return false;

        try
        {
            var cacheItem = await ReadCacheItemAsync<object>(filePath, cancellationToken);
            if (cacheItem.IsExpired)
            {
                File.Delete(filePath);
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                Directory.Delete(_cacheDirectory, true);
                EnsureDirectoryExists();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public override async Task<IEnumerable<string>> GetKeysAsync(string pattern = "*", CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheDirectory))
            return Enumerable.Empty<string>();

        var searchPattern = pattern.Replace("*", "*.cache");
        var files = Directory.GetFiles(_cacheDirectory, searchPattern);
        
        return files.Select(f => Path.GetFileNameWithoutExtension(f))
                   .Where(key => key.StartsWith(_options.KeyPrefix))
                   .Select(key => key.Substring(_options.KeyPrefix.Length));
    }

    private string GetFilePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDirectory, $"{safeKey}.cache");
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    private async Task<FileCacheItem<T>> ReadCacheItemAsync<T>(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var json = await JsonSerializer.DeserializeAsync<FileCacheItem<T>>(stream, cancellationToken: cancellationToken);
        return json;
    }

    private async Task WriteCacheItemAsync<T>(string filePath, FileCacheItem<T> item, CancellationToken cancellationToken)
    {
        using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, item, cancellationToken: cancellationToken);
    }
}

public class FileCacheItem<T>
{
    public T Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    
    public bool IsExpired => ExpiresAt.HasValue && DateTimeOffset.UtcNow > ExpiresAt.Value;
}

public class FileSystemCacheOptions : CacheProviderOptions
{
    public string CacheDirectory { get; set; }
    public int MaxConcurrentOperations { get; set; } = 10;
}
```

### Database Cache Provider

```csharp
public class DatabaseCacheProvider : BaseCacheProvider
{
    private readonly IDbConnection _connection;
    private readonly DatabaseCacheOptions _dbOptions;

    public DatabaseCacheProvider(
        IDbConnection connection,
        ICacheSerializer serializer,
        ILogger<DatabaseCacheProvider> logger,
        DatabaseCacheOptions options)
        : base(serializer, logger, options)
    {
        _connection = connection;
        _dbOptions = options;
        
        InitializeDatabase();
    }

    public override async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedKey = NormalizeKey(key);

        try
        {
            var sql = @"
                SELECT Value, ExpiresAt 
                FROM CacheItems 
                WHERE CacheKey = @Key AND (ExpiresAt IS NULL OR ExpiresAt > @Now)";

            var result = await _connection.QueryFirstOrDefaultAsync(sql, new 
            { 
                Key = normalizedKey, 
                Now = DateTimeOffset.UtcNow 
            });

            if (result == null)
            {
                LogOperation("GET", key, false, stopwatch.Elapsed);
                return default(T);
            }

            var value = _serializer.Deserialize<T>(result.Value);
            LogOperation("GET", key, true, stopwatch.Elapsed);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache item with key {Key}", key);
            LogOperation("GET", key, false, stopwatch.Elapsed);
            return default(T);
        }
    }

    public override async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedKey = NormalizeKey(key);
        var serializedValue = _serializer.Serialize(value);
        var expiresAt = expiration.HasValue ? DateTimeOffset.UtcNow.Add(expiration.Value) : (DateTimeOffset?)null;

        try
        {
            var sql = @"
                INSERT INTO CacheItems (CacheKey, Value, CreatedAt, ExpiresAt)
                VALUES (@Key, @Value, @CreatedAt, @ExpiresAt)
                ON DUPLICATE KEY UPDATE 
                    Value = @Value, 
                    CreatedAt = @CreatedAt, 
                    ExpiresAt = @ExpiresAt";

            await _connection.ExecuteAsync(sql, new
            {
                Key = normalizedKey,
                Value = serializedValue,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt
            });

            LogOperation("SET", key, true, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache item with key {Key}", key);
            LogOperation("SET", key, false, stopwatch.Elapsed);
            throw;
        }
    }

    public override async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedKey = NormalizeKey(key);

        try
        {
            var sql = "DELETE FROM CacheItems WHERE CacheKey = @Key";
            var affected = await _connection.ExecuteAsync(sql, new { Key = normalizedKey });
            
            LogOperation("REMOVE", key, affected > 0, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache item with key {Key}", key);
            LogOperation("REMOVE", key, false, stopwatch.Elapsed);
            throw;
        }
    }

    public override async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var sql = "DELETE FROM CacheItems WHERE CacheKey LIKE @Pattern";
        var dbPattern = pattern.Replace("*", "%");
        await _connection.ExecuteAsync(sql, new { Pattern = dbPattern });
    }

    public override async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        var sql = @"
            SELECT COUNT(1) 
            FROM CacheItems 
            WHERE CacheKey = @Key AND (ExpiresAt IS NULL OR ExpiresAt > @Now)";

        var count = await _connection.QueryFirstAsync<int>(sql, new 
        { 
            Key = normalizedKey, 
            Now = DateTimeOffset.UtcNow 
        });

        return count > 0;
    }

    public override async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var sql = "DELETE FROM CacheItems";
        await _connection.ExecuteAsync(sql);
    }

    public override async Task<IEnumerable<string>> GetKeysAsync(string pattern = "*", CancellationToken cancellationToken = default)
    {
        var sql = "SELECT CacheKey FROM CacheItems WHERE CacheKey LIKE @Pattern";
        var dbPattern = pattern.Replace("*", "%");
        var keys = await _connection.QueryAsync<string>(sql, new { Pattern = dbPattern });
        
        return keys.Where(key => key.StartsWith(_options.KeyPrefix))
                   .Select(key => key.Substring(_options.KeyPrefix.Length));
    }

    private void InitializeDatabase()
    {
        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS CacheItems (
                CacheKey VARCHAR(500) PRIMARY KEY,
                Value LONGTEXT NOT NULL,
                CreatedAt DATETIME NOT NULL,
                ExpiresAt DATETIME NULL,
                INDEX idx_expires (ExpiresAt)
            )";

        _connection.Execute(createTableSql);
    }
}

public class DatabaseCacheOptions : CacheProviderOptions
{
    public string ConnectionString { get; set; }
    public string TableName { get; set; } = "CacheItems";
}
```

## Custom Serializers

Implement custom serialization strategies for specific data types or performance requirements.

### Binary Serializer

```csharp
public class BinarySerializer : ICacheSerializer
{
    private readonly BinarySerializerOptions _options;

    public BinarySerializer(BinarySerializerOptions options)
    {
        _options = options;
    }

    public byte[] Serialize<T>(T obj)
    {
        if (obj == null) return null;

        using var stream = new MemoryStream();
        
        if (_options.UseCompression)
        {
            using var gzipStream = new GZipStream(stream, CompressionLevel.Fastest);
            using var writer = new BinaryWriter(gzipStream);
            SerializeObject(writer, obj);
        }
        else
        {
            using var writer = new BinaryWriter(stream);
            SerializeObject(writer, obj);
        }

        return stream.ToArray();
    }

    public T Deserialize<T>(byte[] data)
    {
        if (data == null || data.Length == 0) return default(T);

        using var stream = new MemoryStream(data);
        
        if (_options.UseCompression)
        {
            using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzipStream);
            return (T)DeserializeObject(reader, typeof(T));
        }
        else
        {
            using var reader = new BinaryReader(stream);
            return (T)DeserializeObject(reader, typeof(T));
        }
    }

    private void SerializeObject(BinaryWriter writer, object obj)
    {
        var type = obj.GetType();
        
        // Write type information
        writer.Write(type.AssemblyQualifiedName);
        
        // Handle primitive types
        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            SerializePrimitive(writer, obj);
            return;
        }

        // Handle collections
        if (obj is IEnumerable enumerable && type != typeof(string))
        {
            SerializeCollection(writer, enumerable);
            return;
        }

        // Handle complex objects using reflection
        SerializeComplexObject(writer, obj);
    }

    private object DeserializeObject(BinaryReader reader, Type expectedType)
    {
        var typeName = reader.ReadString();
        var actualType = Type.GetType(typeName);

        if (actualType != expectedType && !expectedType.IsAssignableFrom(actualType))
        {
            throw new InvalidOperationException($"Type mismatch: expected {expectedType}, got {actualType}");
        }

        // Handle primitive types
        if (actualType.IsPrimitive || actualType == typeof(string) || actualType == typeof(DateTime) || actualType == typeof(DateTimeOffset))
        {
            return DeserializePrimitive(reader, actualType);
        }

        // Handle collections
        if (typeof(IEnumerable).IsAssignableFrom(actualType) && actualType != typeof(string))
        {
            return DeserializeCollection(reader, actualType);
        }

        // Handle complex objects
        return DeserializeComplexObject(reader, actualType);
    }

    private void SerializePrimitive(BinaryWriter writer, object obj)
    {
        switch (obj)
        {
            case bool b: writer.Write(b); break;
            case byte b: writer.Write(b); break;
            case sbyte sb: writer.Write(sb); break;
            case short s: writer.Write(s); break;
            case ushort us: writer.Write(us); break;
            case int i: writer.Write(i); break;
            case uint ui: writer.Write(ui); break;
            case long l: writer.Write(l); break;
            case ulong ul: writer.Write(ul); break;
            case float f: writer.Write(f); break;
            case double d: writer.Write(d); break;
            case decimal dec: writer.Write(dec); break;
            case char c: writer.Write(c); break;
            case string str: writer.Write(str ?? string.Empty); break;
            case DateTime dt: writer.Write(dt.ToBinary()); break;
            case DateTimeOffset dto: 
                writer.Write(dto.DateTime.ToBinary());
                writer.Write(dto.Offset.Ticks);
                break;
        }
    }

    private object DeserializePrimitive(BinaryReader reader, Type type)
    {
        return type.Name switch
        {
            nameof(Boolean) => reader.ReadBoolean(),
            nameof(Byte) => reader.ReadByte(),
            nameof(SByte) => reader.ReadSByte(),
            nameof(Int16) => reader.ReadInt16(),
            nameof(UInt16) => reader.ReadUInt16(),
            nameof(Int32) => reader.ReadInt32(),
            nameof(UInt32) => reader.ReadUInt32(),
            nameof(Int64) => reader.ReadInt64(),
            nameof(UInt64) => reader.ReadUInt64(),
            nameof(Single) => reader.ReadSingle(),
            nameof(Double) => reader.ReadDouble(),
            nameof(Decimal) => reader.ReadDecimal(),
            nameof(Char) => reader.ReadChar(),
            nameof(String) => reader.ReadString(),
            nameof(DateTime) => DateTime.FromBinary(reader.ReadInt64()),
            nameof(DateTimeOffset) => new DateTimeOffset(DateTime.FromBinary(reader.ReadInt64()), new TimeSpan(reader.ReadInt64())),
            _ => throw new NotSupportedException($"Type {type} is not supported for primitive deserialization")
        };
    }
}

public class BinarySerializerOptions
{
    public bool UseCompression { get; set; } = true;
    public bool IncludeTypeInformation { get; set; } = true;
    public bool UseNetworkByteOrder { get; set; } = false;
}
```

### Protocol Buffer Serializer

```csharp
public class ProtocolBufferSerializer : ICacheSerializer
{
    private readonly ProtobufSerializerOptions _options;

    public ProtocolBufferSerializer(ProtobufSerializerOptions options)
    {
        _options = options;
    }

    public byte[] Serialize<T>(T obj)
    {
        if (obj == null) return null;

        using var stream = new MemoryStream();
        
        if (_options.UseCompression)
        {
            using var gzipStream = new GZipStream(stream, CompressionLevel.Fastest);
            Serializer.Serialize(gzipStream, obj);
        }
        else
        {
            Serializer.Serialize(stream, obj);
        }

        return stream.ToArray();
    }

    public T Deserialize<T>(byte[] data)
    {
        if (data == null || data.Length == 0) return default(T);

        using var stream = new MemoryStream(data);
        
        if (_options.UseCompression)
        {
            using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
            return Serializer.Deserialize<T>(gzipStream);
        }
        else
        {
            return Serializer.Deserialize<T>(stream);
        }
    }
}

public class ProtobufSerializerOptions
{
    public bool UseCompression { get; set; } = false;
    public bool PreserveReferences { get; set; } = false;
}
```

## Custom Key Generators

Create custom cache key generation strategies.

### Hashed Key Generator

```csharp
public class HashedKeyGenerator : ICacheKeyGenerator
{
    private readonly HashedKeyOptions _options;

    public HashedKeyGenerator(HashedKeyOptions options)
    {
        _options = options;
    }

    public string GenerateKey(ControllerActionDescriptor actionDescriptor, object[] parameters)
    {
        var baseKey = GenerateBaseKey(actionDescriptor, parameters);
        
        if (baseKey.Length <= _options.MaxKeyLength)
        {
            return baseKey;
        }

        // Hash long keys
        return GenerateHashedKey(baseKey);
    }

    private string GenerateBaseKey(ControllerActionDescriptor actionDescriptor, object[] parameters)
    {
        var keyBuilder = new StringBuilder();
        
        // Add controller name
        keyBuilder.Append(GetControllerName(actionDescriptor.ControllerName));
        keyBuilder.Append(':');
        
        // Add action name
        keyBuilder.Append(GetActionName(actionDescriptor.ActionName));
        
        // Add parameters
        if (parameters?.Length > 0)
        {
            keyBuilder.Append(':');
            keyBuilder.Append(GetParametersHash(parameters));
        }

        return keyBuilder.ToString();
    }

    private string GenerateHashedKey(string originalKey)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(originalKey));
        var hash = Convert.ToBase64String(hashBytes);
        
        // Make hash URL-safe
        hash = hash.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        
        return $"hash:{hash}";
    }

    private string GetControllerName(string fullControllerName)
    {
        var name = fullControllerName.EndsWith("Controller")
            ? fullControllerName[..^10] // Remove "Controller" suffix
            : fullControllerName;

        return _options.UseShortNames ? GetShortName(name) : name;
    }

    private string GetActionName(string actionName)
    {
        return _options.UseShortNames ? GetShortName(actionName) : actionName;
    }

    private string GetParametersHash(object[] parameters)
    {
        var parameterStrings = parameters.Select(p => SerializeParameter(p));
        var combined = string.Join("|", parameterStrings);
        
        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hashBytes)[..8]; // Use first 8 characters
    }

    private string SerializeParameter(object parameter)
    {
        return parameter switch
        {
            null => "null",
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(parameter)
        };
    }

    private string GetShortName(string name)
    {
        // Generate short names for common patterns
        return name switch
        {
            "User" or "Users" => "Usr",
            "Product" or "Products" => "Prd",
            "Order" or "Orders" => "Ord",
            "Customer" or "Customers" => "Cst",
            "Category" or "Categories" => "Cat",
            "Get" or "GetAll" or "GetList" => "G",
            "Create" or "Post" or "Add" => "C",
            "Update" or "Put" or "Edit" => "U",
            "Delete" or "Remove" => "D",
            _ when name.Length > 10 => name[..3] + name.GetHashCode().ToString("x")[..2],
            _ => name
        };
    }
}

public class HashedKeyOptions
{
    public int MaxKeyLength { get; set; } = 100;
    public bool UseShortNames { get; set; } = true;
    public bool IncludeNamespace { get; set; } = false;
    public string KeyPrefix { get; set; } = "";
}
```

### Hierarchical Key Generator

```csharp
public class HierarchicalKeyGenerator : ICacheKeyGenerator
{
    private readonly HierarchicalKeyOptions _options;

    public string GenerateKey(ControllerActionDescriptor actionDescriptor, object[] parameters)
    {
        var segments = new List<string>();
        
        // Add namespace if configured
        if (_options.IncludeNamespace)
        {
            segments.Add(GetNamespace(actionDescriptor));
        }
        
        // Add controller
        segments.Add(GetControllerSegment(actionDescriptor));
        
        // Add action
        segments.Add(GetActionSegment(actionDescriptor));
        
        // Add parameter segments
        if (parameters?.Length > 0)
        {
            segments.AddRange(GetParameterSegments(actionDescriptor, parameters));
        }

        return string.Join(_options.Separator, segments);
    }

    private string GetNamespace(ControllerActionDescriptor actionDescriptor)
    {
        return actionDescriptor.ControllerTypeInfo.Namespace?.Split('.').LastOrDefault() ?? "Default";
    }

    private string GetControllerSegment(ControllerActionDescriptor actionDescriptor)
    {
        var controllerName = actionDescriptor.ControllerName;
        
        return _options.ControllerNaming switch
        {
            NamingConvention.PascalCase => controllerName,
            NamingConvention.CamelCase => ToCamelCase(controllerName),
            NamingConvention.KebabCase => ToKebabCase(controllerName),
            NamingConvention.SnakeCase => ToSnakeCase(controllerName),
            _ => controllerName.ToLowerInvariant()
        };
    }

    private string GetActionSegment(ControllerActionDescriptor actionDescriptor)
    {
        var actionName = actionDescriptor.ActionName;
        
        return _options.ActionNaming switch
        {
            NamingConvention.PascalCase => actionName,
            NamingConvention.CamelCase => ToCamelCase(actionName),
            NamingConvention.KebabCase => ToKebabCase(actionName),
            NamingConvention.SnakeCase => ToSnakeCase(actionName),
            _ => actionName.ToLowerInvariant()
        };
    }

    private IEnumerable<string> GetParameterSegments(ControllerActionDescriptor actionDescriptor, object[] parameters)
    {
        var parameterInfos = actionDescriptor.MethodInfo.GetParameters();
        
        for (int i = 0; i < Math.Min(parameterInfos.Length, parameters.Length); i++)
        {
            var paramInfo = parameterInfos[i];
            var paramValue = parameters[i];
            
            if (ShouldIncludeParameter(paramInfo, paramValue))
            {
                yield return FormatParameterValue(paramInfo.Name, paramValue);
            }
        }
    }

    private bool ShouldIncludeParameter(ParameterInfo paramInfo, object value)
    {
        // Skip complex objects unless specifically configured
        if (value != null && !IsSimpleType(value.GetType()))
        {
            return _options.IncludeComplexParameters;
        }

        // Skip null values unless configured
        if (value == null)
        {
            return _options.IncludeNullParameters;
        }

        return true;
    }

    private string FormatParameterValue(string paramName, object value)
    {
        if (value == null) return "null";
        
        if (_options.IncludeParameterNames)
        {
            return $"{paramName}{_options.ParameterValueSeparator}{value}";
        }
        
        return value.ToString();
    }

    private bool IsSimpleType(Type type)
    {
        return type.IsPrimitive || 
               type == typeof(string) || 
               type == typeof(DateTime) || 
               type == typeof(DateTimeOffset) || 
               type == typeof(Guid) ||
               type.IsEnum ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && IsSimpleType(type.GetGenericArguments()[0]));
    }

    private string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return char.ToLowerInvariant(input[0]) + input[1..];
    }

    private string ToKebabCase(string input)
    {
        return Regex.Replace(input, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
    }

    private string ToSnakeCase(string input)
    {
        return Regex.Replace(input, "([a-z])([A-Z])", "$1_$2").ToLowerInvariant();
    }
}

public class HierarchicalKeyOptions
{
    public string Separator { get; set; } = ":";
    public string ParameterValueSeparator { get; set; } = "=";
    public bool IncludeNamespace { get; set; } = false;
    public bool IncludeParameterNames { get; set; } = false;
    public bool IncludeComplexParameters { get; set; } = false;
    public bool IncludeNullParameters { get; set; } = false;
    public NamingConvention ControllerNaming { get; set; } = NamingConvention.PascalCase;
    public NamingConvention ActionNaming { get; set; } = NamingConvention.PascalCase;
}

public enum NamingConvention
{
    PascalCase,
    CamelCase,
    KebabCase,
    SnakeCase,
    LowerCase
}
```

## Registration and Configuration

Register custom providers with dependency injection.

### Custom Provider Registration

```csharp
// Extension method for easy registration
public static class CustomProviderExtensions
{
    public static IServiceCollection AddFileSystemCache(
        this IServiceCollection services, 
        Action<FileSystemCacheOptions> configureOptions = null)
    {
        var options = new FileSystemCacheOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICacheSerializer, JsonCacheSerializer>();
        services.AddSingleton<ICacheProvider, FileSystemCacheProvider>();
        
        return services;
    }

    public static IServiceCollection AddDatabaseCache(
        this IServiceCollection services,
        string connectionString,
        Action<DatabaseCacheOptions> configureOptions = null)
    {
        var options = new DatabaseCacheOptions { ConnectionString = connectionString };
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IDbConnection>(provider => new MySqlConnection(connectionString));
        services.AddSingleton<ICacheSerializer, JsonCacheSerializer>();
        services.AddSingleton<ICacheProvider, DatabaseCacheProvider>();
        
        return services;
    }

    public static IServiceCollection AddCustomKeyGenerator<T>(
        this IServiceCollection services)
        where T : class, ICacheKeyGenerator
    {
        services.AddSingleton<ICacheKeyGenerator, T>();
        return services;
    }

    public static IServiceCollection AddCustomSerializer<T>(
        this IServiceCollection services)
        where T : class, ICacheSerializer
    {
        services.AddSingleton<ICacheSerializer, T>();
        return services;
    }
}

// Usage in Program.cs
builder.Services.AddFileSystemCache(options =>
{
    options.CacheDirectory = "/app/cache";
    options.KeyPrefix = "myapp:";
    options.EnableLogging = true;
});

builder.Services.AddCustomKeyGenerator<HashedKeyGenerator>();
builder.Services.AddCustomSerializer<ProtocolBufferSerializer>();
```

### Multi-Provider Setup

```csharp
public class MultiTierCacheProvider : ICacheProvider
{
    private readonly ICacheProvider _l1Cache; // Fast, small cache (memory)
    private readonly ICacheProvider _l2Cache; // Slower, larger cache (Redis/Database)
    private readonly MultiTierOptions _options;

    public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        // Try L1 cache first
        var result = await _l1Cache.GetAsync<T>(key, cancellationToken);
        if (result != null) return result;

        // Try L2 cache
        result = await _l2Cache.GetAsync<T>(key, cancellationToken);
        if (result != null)
        {
            // Promote to L1 cache
            await _l1Cache.SetAsync(key, result, _options.L1Expiration, cancellationToken);
        }

        return result;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        // Set in both caches
        var l1Expiration = _options.L1Expiration ?? expiration;
        var l2Expiration = expiration;

        await Task.WhenAll(
            _l1Cache.SetAsync(key, value, l1Expiration, cancellationToken),
            _l2Cache.SetAsync(key, value, l2Expiration, cancellationToken)
        );
    }

    // Other methods follow similar pattern...
}
```

For integration and testing:
- **[Testing Strategies]({{ site.baseurl }}/advanced/testing-strategies)** - Test custom providers
- **[Installation]({{ site.baseurl }}/getting-started/installation)** - Setup and configuration
- **[Performance Tuning]({{ site.baseurl }}/performance/production-tuning)** - Optimize custom implementations