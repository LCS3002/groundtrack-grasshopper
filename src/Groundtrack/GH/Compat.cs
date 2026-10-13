using System;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace Groundtrack
{
    /// <summary>Definitions saved before a component gained inputs or outputs: read the ones they
    /// have, and the new ones keep their defaults (instead of Grasshopper's "parameter chunk is
    /// missing" error and a half-read component).</summary>
    static class Compat
    {
        public static bool Read(GH_Component c, GH_IReader reader, Func<GH_IReader, bool> baseRead)
        {
            int ins = Count(reader, "param_input"), outs = Count(reader, "param_output");
            var newIns = ins > 0 && ins < c.Params.Input.Count ? c.Params.Input.Skip(ins).ToList() : null;
            var newOuts = outs > 0 && outs < c.Params.Output.Count ? c.Params.Output.Skip(outs).ToList() : null;
            if (newIns == null && newOuts == null) return baseRead(reader);
            if (newIns != null) foreach (var p in newIns) c.Params.UnregisterInputParameter(p, false);
            if (newOuts != null) foreach (var p in newOuts) c.Params.UnregisterOutputParameter(p, false);
            bool ok = baseRead(reader);
            if (newIns != null) foreach (var p in newIns) c.Params.RegisterInputParam(p);
            if (newOuts != null) foreach (var p in newOuts) c.Params.RegisterOutputParam(p);
            c.Params.OnParametersChanged();
            return ok;
        }

        static int Count(GH_IReader reader, string name)
        {
            int n = 0;
            while (reader.ChunkExists(name, n)) n++;
            return n;
        }
    }
}
