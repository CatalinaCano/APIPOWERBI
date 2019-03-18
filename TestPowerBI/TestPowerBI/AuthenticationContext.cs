using System;
using System.Threading.Tasks;

namespace TestPowerBI
{
    internal class AuthenticationContext
    {
        private string authorityUrl;

        public AuthenticationContext(string authorityUrl)
        {
            this.authorityUrl = authorityUrl;
        }

        internal Task AcquireTokenAsync(string resourceUrl, string applicationId, UserPasswordCredential credential)
        {
            throw new NotImplementedException();
        }
    }
}