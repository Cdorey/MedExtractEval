namespace MedExtract.Base
{
    public static class ConnectionStringResolvers
    {
        public static ConnectionStringResolver FromValue(string cs) =>
            () => cs;

        public static ConnectionStringResolver FromEnvironment(string envName, bool required = true) =>
            () =>
            {
                var cs = Environment.GetEnvironmentVariable(envName);
                if (required && string.IsNullOrWhiteSpace(cs))
                    throw new InvalidOperationException($"Environment variable '{envName}' is empty.");
                return cs ?? string.Empty;
            };

        /// <summary>
        /// 可选 helper：仅给 CLI 用；不要在类库核心流程里强依赖它
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public static ConnectionStringResolver ConsolePrompt(string prompt = "Connection string: ") =>
            () =>
            {
                Console.Write(prompt);
                var cs = Console.ReadLine();
                return cs ?? string.Empty;
            };
    }
}
