using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playbook.Application.Historical;
using Playbook.Core.Draft;

namespace Playbook.Infrastructure.Historical;

/// <summary>Small file-backed store on the same PLAYBOOK_DATA_DIR volume as historical drafts.</summary>
public sealed class PersonalDraftKnowledgeStore : IPersonalDraftKnowledgeStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<PersonalDraftKnowledgeStore> _logger;
    private readonly object _gate = new();
    public string StorePath { get; }

    public PersonalDraftKnowledgeStore(ILogger<PersonalDraftKnowledgeStore> logger, string? fileName = null)
    {
        _logger = logger;
        var root = Environment.GetEnvironmentVariable("PLAYBOOK_DATA_DIR");
        root = string.IsNullOrWhiteSpace(root) ? Path.Combine(AppContext.BaseDirectory, "data") : root;
        Directory.CreateDirectory(root);
        StorePath = Path.Combine(root, fileName ?? "personal-draft-knowledge.json");
    }

    public IReadOnlyList<PersonalDraftKnowledge> Load()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(StorePath)
                    ? JsonSerializer.Deserialize<List<PersonalDraftKnowledge>>(File.ReadAllText(StorePath), Options) ?? []
                    : [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to load personal draft knowledge from {Path}", StorePath);
                return [];
            }
        }
    }

    public void Save(IReadOnlyList<PersonalDraftKnowledge> knowledge)
    {
        lock (_gate)
        {
            try
            {
                File.WriteAllText(StorePath, JsonSerializer.Serialize(knowledge, Options));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to persist personal draft knowledge to {Path}", StorePath);
                throw;
            }
        }
    }
}
