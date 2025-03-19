using Stride.Core.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TR.Stride.Ocean
{
    static class ProfilingKeys
    {
        public static readonly ProfilingKey Ocean = new ProfilingKey("Ocean");
        public static readonly ProfilingKey CalculateInitials = new ProfilingKey("Ocean.CalculateInitials");
        public static readonly ProfilingKey CalculateWavesAtTime = new ProfilingKey("Ocean.CalculateWavesAtTime");
        public static readonly ProfilingKey GenearteMipMaps = new ProfilingKey("Ocean.GenearteMipMaps");
    }
}
