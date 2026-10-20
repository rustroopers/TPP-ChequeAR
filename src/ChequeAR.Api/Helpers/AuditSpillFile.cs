using System;
using System.IO;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Newtonsoft.Json;

namespace ChequeAR.Api.Helpers
{
    public sealed class AuditSpillFile
    {
        private const string Prefix = "audit-pendiente-";
        private readonly string _dir;
        private readonly object _gate = new object();

        public AuditSpillFile(string dir)
        {
            _dir = dir;
        }

        public void Write(AuditEvent evt)
        {
            var line = JsonConvert.SerializeObject(evt, Formatting.None);
            var path = Path.Combine(_dir, Prefix + DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");
            lock (_gate)
            {
                Directory.CreateDirectory(_dir);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }

        public int Replay(AuditDispatcher audit, Action<Exception, string> onError)
        {
            if (!Directory.Exists(_dir)) return 0;
            var total = 0;
            foreach (var file in Directory.GetFiles(_dir, Prefix + "*.jsonl"))
            {
                var taken = file + ".reenviando";
                try
                {
                    File.Move(file, taken);
                    foreach (var line in File.ReadAllLines(taken))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var evt = JsonConvert.DeserializeObject<AuditEvent>(line);
                        if (evt == null) continue;
                        audit.Enqueue(evt);
                        total++;
                    }
                    File.Delete(taken);
                }
                catch (Exception ex)
                {
                    onError(ex, "No se pudo reenviar la auditoría pendiente de " + Path.GetFileName(file) + ".");
                }
            }
            return total;
        }
    }
}
