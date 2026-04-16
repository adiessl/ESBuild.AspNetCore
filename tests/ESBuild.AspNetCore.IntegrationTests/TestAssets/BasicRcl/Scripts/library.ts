export function getLibraryMessage(name: string): string {
    return `BasicRcl says hello to ${name}`;
}

console.log(getLibraryMessage("static-web-assets"));
