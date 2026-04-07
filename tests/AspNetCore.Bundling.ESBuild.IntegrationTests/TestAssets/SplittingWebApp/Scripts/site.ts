async function boot(): Promise<void> {
    const featureModule = await import("./feature");
    console.log(featureModule.getFeatureMessage());
}

void boot();
