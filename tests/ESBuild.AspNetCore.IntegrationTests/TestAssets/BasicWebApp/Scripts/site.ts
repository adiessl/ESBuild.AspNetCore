const greeting = "Hello from ESBuild.AspNetCore";

function formatGreeting(name: string): string {
    return `${greeting} ${name}`;
}

const names = ["sample", "integration", "test"];

for (const name of names) {
    console.log(formatGreeting(name));
}
