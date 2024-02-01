using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedisRPC_Test
{
    public class TestModels
    {
        public class MyModel
        {
            public int A { get; set; }
            public long B { get; set; }
            public string C { get; set; }
            public Dictionary<string, List<string>> D { get; set; }
            public SubModel E { get; set; }
        }

        public class SubModel
        {
            public string A { get; set; }
        }
    }
}
