> **LATEST FOR VERSION** see: https://github.com/capesean/codegenerator3

# codegenerator
Generate code for an Entity Framework 6 / WebApi, with an AngularJS font-end, using Bootstrap 3 or 4.

## Online demo!
A demo of this system is available online at: http://codegenerator.sitedemo.co.za/

You can log into the demo, create your own projects, with entities, fields, relationships, lookups, etc. Then you can view the code that gets generated. It can generate:

* Model: tied into the EF6 DbContext, so EF will generate & update your tables automatically!
* DTO: The WebApi will use the DTO to safely export & import data.
* Controller: A fully fledged WebApi controller with methods like Search/Query, Get, Insert, Update, Delete, Sort, etc.
* DbContext: codegenerator will link your entities into the DbContext, as well as use the Fluent API for field definitions where attributes are not enough (e.g. decimal precision)
* SettingsDTO: your enums and general application settings get sent to the AngularJs client on login using a Settings DTO.
* BundleConfig: codegenerator will automatically add the necessary files to the bundles, so you just have to rebuild after adding an entity, and your project will include the relevant javascript files!
* Enums: Create enums via codegenerator, which can be used as fields in your tables/entities.
* AppRouter: Your entities will get added to the AngularJs router, using $stateProvider. 
* ApiResource: each entity gets added as an $resource factory in your app, so you can easily access the API by just injecting the appropriate resource.
* List Html: codegenerator will produce a search page for the records in your table.
* List TypeScript: you'll also get the TypeScript file that has the controller for the List Html page.
* Edit Html: codegenerator will produce an edit page for adding/editing/deleting a record in your database.
* Edit TypeScript: you'll also get the TypeScript file with the controller for the Edit page.

That just begins to scratch the surface! You can also have lookups, relationships, hierarchies, sort orders, Min/Max lengths on fields, define unique constraints, primary keys, search fields, field behavious (read only/edit-when-new/etc), Code Replacements (which can modify the generated code, so your customised changes are retained), and much more! 

If it's running on your own machine, you can deploy the code to your project at the click of a button!

## Blog post
There's a (slightly) old blogpost here, that describes the system in a bit more detail:
http://capesean.co.za/blog/angularjs-typescipt-webapi-entity-framework-code-generation/

## Latest 'project shell'
Note that, to run properly as a project/website, the generated code requires a 'project shell' with the surrounding controllers, stylesheets, directives, packages, etc. 

The codegenerator project itself mostly contains these files, as it's (generated by the tool itself)[http://knowyourmeme.com/memes/we-need-to-go-deeper]. 

However, I don't keep codegenerator's project files fully up to date (I'm not sure how many people are interested in it?), so I just use codegenerator to generate the code to use in other, up-to-date 'project shells'. 

If you're interested in using this tool, get in touch with me, and I can provide you with the latest shell. I'm constantly adding new features (like the 'Use Selector Directive' on the Relationships). That feature doesn't exist in the CodeGenerator shell, but I now use it extensively in other projects.

So if you're interested, get in touch via Github or (Twitter)[https://twitter.com/capesean]. I'd love to know that someone else is interested in using this highly-productive tool!

