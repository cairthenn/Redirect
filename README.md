# Redirect
 
Redirect is a [Dalamud](https://github.com/goatcorp/Dalamud) plugin for Final Fantasy XIV Online. 

The plugin enables seamless use of mouseover and focus target functionality by allowing you to change the base targeting priority of each action on your bars.

Additionally, it enables action queueing for things that the game normally won't cover, including macros, Sprint, and combat potions.

![Redirect preview](https://github.com/cairthenn/Redirect/blob/main/preview.png?raw=true)

### How do I install it?

This plugin is currently available through the Dalamud plugin installer. If you are having trouble installing it, I also maintain a mirror at my [custom plugin repository](https://github.com/cairthenn/CairDalamudPlugins).

### Commands

This plugin has a single command, `/redirect`, that opens the configuration. In addition to the standard configuration, there is an options menu that provides some additional features.

### Options menu

These options let you control how Redirect handles target changing:

* `Ignore targets out of range` : Incurs a distance check on potential target changes. If the action is redirected to a target out of range, that target is ignored, and the next choice will be attempted
* `Ignore incorrect target types`: Incurs a target type check on potential target changes. Prevents trying to use friendly spells on hostile targets and vice versa
* `Treat all <friendly/hostile> actions as mouseovers` : Treats all actions of the specified type as UI mouseover candidates by default
  * `Include <friendly/hostile> target models` : Treats all actions of the specified type as model mouseover candidates as well
  * `Include ground targets at cursor` : All actions that use cursor placement (Asylum, Earthly Star, Sacred Soil) will be instantly placed at the mouse cursor

These options allow additional things to enter the combat queue, avoiding "clipping" the GCD:

* `Ground targeted actions` : Lets you queue ground actions while casting. This must be used in conjunction with a target changing option -- the orange ground targeting circle will *not* appear
* `Actions from macros` : Prevents GCD clipping from macro actions
* `Sprint` : Zoom even easier
* `Potions` : Includes various stat potions and elixirs. Does **not** include food

## FAQ

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

### Why are lower level versions of spells listed? Can you combine them?

This is primarily due to the way the action bar handles upgrading spells automatically for synced content. While it is technically possible to combine them, there may be situations where this behavior is undesirable and will be left as is for now. 

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

**Notice**: This is not setup to allow you to create one-button macros that will play the game for you, and actually explicitly prevents it. If you use a macro that has multiple actions that can succeed while you are not casting, it will use the first one immediately *and* queue the second one. This is the intended behavior.


### I have a different problem / I want to suggest something!

Please create an issue if one doesn't exist already. Keep in mind that requests aren't guaranteed to be fulfilled!
