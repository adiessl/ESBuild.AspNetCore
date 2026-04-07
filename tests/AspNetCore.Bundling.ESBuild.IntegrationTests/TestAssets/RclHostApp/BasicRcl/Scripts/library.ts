export function getHostedLibraryMessage(name: string): string {
    return `Hosted BasicRcl says hello to ${name}`;
}

console.log(getHostedLibraryMessage("publish"));
