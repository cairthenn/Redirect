# Redirect
 
Redirect is a [Dalamud](https://github.com/goatcorp/Dalamud) plugin for Final Fantasy XIV Online. 

The plugin enables seamless use of mouseover and focus target functionality by allowing you to change the base targeting priority of each action on your bars.

### Commands

This plugin has a single command, `/redirect`, that opens the configuration.

### How do I setup a UI mouseover?

Open the plugin configuration and select the job you are interested in setting up a mouseover for. Scroll through the action list, or use the search feature, to locate the action you wish to modify. If you cannot find the ability, it is not currently supported.

Once you have located the action, click the + button next to it. You are now done! If you mousover a UI element while using that spell, it will cast there instead. If not, the default target for the action will be attempted.

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

The final target is selected based on a priority system from top to bottom. Once a match is made, that target will be used and anything below it will be ignore. If no match is made, the default target for the action will be attempted.

### What actions are supported?

Just about anything you can use on a different target or place on the ground is supported. This excludes things like Lost Actions, or any Duty Action. If you think an action should be supported and isn't, feel free to create an issue.

### Why do SCH/SMN have [...]?

Arcanist is weird.

### I have a different problem / I want to suggest something!

Please create an issue if one doesn't exist already. Keep in mind that requests aren't guaranteed to be fulfilled!
