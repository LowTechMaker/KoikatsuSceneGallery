# SceneGallery plugin packages

`SceneGallery.PluginSdk` is the binary contract shared by SceneGallery and its plugins. Plugin projects should consume its compile asset while excluding its runtime asset because the host supplies the contract assembly.

`SceneGallery.PluginCommon` and `SceneGallery.PluginCommon.Secrets` are source-only packages. Their `contentFiles` are compiled into each consuming plugin, and all supplied types are internal to that plugin assembly.

Source and release workflows are maintained in the [KoikatsuSceneGallery repository](https://github.com/LowTechMaker/KoikatsuSceneGallery).
