const greeting = "Hello from AspNetCore.Bundling.ESBuild";

function formatGreeting(name: string): string {
    return `${greeting} ${name}`;
}

const names = ["sample", "integration", "test"];

for (const name of names) {
    console.log(formatGreeting(name));
}
