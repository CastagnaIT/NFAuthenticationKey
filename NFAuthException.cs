using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NFAuthenticationKey
{
    [Serializable]
    public class NFAuthException : Exception
    {
        public NFAuthException()
        { }

        public NFAuthException(string message)
            : base(message)
        { }

        public NFAuthException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
