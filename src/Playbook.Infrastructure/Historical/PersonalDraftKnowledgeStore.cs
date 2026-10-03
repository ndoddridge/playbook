using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playbook.Application.Historical;
using Playbook.Application.Users;
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
    private readonly object _gate;
    public string StorePath { get; }

    public PersonalDraftKnowledgeStore(
        ILogger<PersonalDraftKnowledgeStore> logger,
        string? fileName = null,
        IUserContext? userContext = null,
        string? rootOverride = null)
    {
        _logger = logger;
        StorePath = UserDataPaths.UserFile(rootOverride ?? UserDataPaths.DataRoot(), userContext, fileName ?? "personal-draft-knowledge.json");
        _gate = UserDataPaths.LockFor(StorePath);
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
