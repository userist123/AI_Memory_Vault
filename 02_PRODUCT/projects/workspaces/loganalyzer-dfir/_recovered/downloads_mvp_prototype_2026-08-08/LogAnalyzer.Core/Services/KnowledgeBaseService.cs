using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Services
{
    public class KnowledgeBaseService
    {
        private readonly Dictionary<int, EventKnowledgeItem> _kbDictionary = new Dictionary<int, EventKnowledgeItem>();
        public List<DetectionRule> Reguli { get; private set; } = new List<DetectionRule>();

        public void LoadCategories(string categoriesFolderPath)
        {
            if (!Directory.Exists(categoriesFolderPath)) return;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var jsonFiles = Directory.GetFiles(categoriesFolderPath, "*.json");
            foreach (var file in jsonFiles)
            {
                try
                {
                    string jsonContent = File.ReadAllText(file);

                    if (Path.GetFileName(file).Equals("knowledge_base_extins.json", StringComparison.OrdinalIgnoreCase))
                    {
                        var reguli = JsonSerializer.Deserialize<List<DetectionRule>>(jsonContent, jsonOptions);
                        if (reguli != null)
                        {
                            Reguli.AddRange(reguli);
                            foreach (var regula in reguli)
                            {
                                foreach (var eid in regula.EventIduri)
                                {
                                    if (!_kbDictionary.ContainsKey(eid))
                                    {
                                        _kbDictionary[eid] = new EventKnowledgeItem
                                        {
                                            Category = regula.Titlu,
                                            EventID = eid.ToString(),
                                            MitreTTP = regula.Mitre.TehnicaId
                                        };
                                    }
                                }
                            }
                        }
                        continue;
                    }

                    var items = JsonSerializer.Deserialize<List<EventKnowledgeItem>>(jsonContent, jsonOptions);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            if (int.TryParse(item.EventID, out int eid) && !_kbDictionary.ContainsKey(eid))
                            {
                                _kbDictionary[eid] = item;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignora problemele de formatare per fisier - nu opreste incarcarea celorlalte.
                }
            }
        }

        public EventKnowledgeItem? GetDetails(int eventId)
        {
            _kbDictionary.TryGetValue(eventId, out var item);
            return item;
        }
    }
}
