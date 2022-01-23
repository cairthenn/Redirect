# Redirect
 
Redirect is a [Dalamud](https://github.com/goatcorp/Dalamud) plugin for Final Fantasy XIV Online. 

The plugin enables seamless use of mouseover and focus target functionality by allowing you to change the base targeting priority of each action on your bars.

![Redirect preview](https://github.com/cairthenn/Redirect/blob/main/preview.png?raw=true)

### Commands

This plugin has a single command, `/redirect`, that opens the configuration.

### About macro queueing

This plugin allows you to "queue" actions using macros as you normally would be able to via the action bar. This does not bypass the game's queue system or allow you to queue multiple things at the same time. It does, however, allow you to create priority-based macros or macros that use custom targeting without worrying about clipping.

For example, you can create a Raise macro that will always try to use Swiftcast and then Raise your moused-over target:

```
/macroicon Raise
/ac Swiftcast
/ac Raise <mo>
```

Normally, if you try to use this macro while casting, nothing will happen. With macro queueing enabled, it will try to queue Swiftcast, and if it isn't available, it will try to queue Raise.

Note that if you also have custom action targeting enabled in the configuration, it will override your macro's intended target. However, this system allows you to completely avoid the configuration step altogether and simply play using normal ingame macros that now work as though they were action bar abilities!

### Can I queue sprint and items?

Although it is possible, the game is actually coded to explicitly prevent this. More research is needed to implement this in a safe manner.


### How do I setup a UI mouseover?

Open the plugin configuration and select the job you are interested in setting up a mouseover for. Scroll through the action list, or use the search feature, to locate the action you wish to modify. If you cannot find the ability, it is not currently supported.

Once you have located the action, click the + button next to it. Assuming the drop down box that appeared says "UI Mouseover", you are now done! If you mouseover a UI element while using that spell, it will cast there instead. If not, the default target for the action will be attempted.

### What target options are available?

The following are currently supported options:

 * `Cursor`: Places the action at the mouse cursor location
 * `UI Mouseover`: Currently moused-over UI element
 * `Model Mouseover`: Currently moused-over game element, such as a character model or monster model
 * `Self`: The player
 * `Target`: Your current target
 * `Focus`: Your current focus target
 * `Target of Target`: Your target's target
 * `<2>` through `<8>`: Party member 2-8

### Why can I add more than one target option to a single action?

The final target is selected based on a priority system from top to bottom. Once a match is made, that target will be used and anything below it will be ignored. If no match is made, the default target for the action will be attempted.

### What actions are supported?

Just about anything you can use on a different target or place on the ground is supported. This excludes things like Lost Actions, or any Duty Action. If you think an action should be supported and isn't, feel free to create an issue.

### Why do SCH/SMN have [...]?

Arcanist is weird.

### I have a different problem / I want to suggest something!

Please create an issue if one doesn't exist already. Keep in mind that requests aren't guaranteed to be fulfilled!
