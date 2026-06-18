---
name: event-manager-skill
description: This skill explain how to retrieve events and create update and delete events
---

## When To Use

When users ask about events next to the standard DataMiner events you should also look into these type of events, these are typically SDM events

## Retrieving events

In order to retrieve events you should call the  Events.Get Events data source

Always format the returned events in a nice table

## Creating updating deleting events

When user ask to update create or delete event
1. ALWAYS confirm the name of the event
2. ALWAYS show what fields will be changed
3. ALWAYS ask for permission to do the  change
4. MAKE SURE you got permission after you let the user know which fields you're going to change

## Updating fields of events

1. perform a get via the script tool to retrieve the json of the event
2. udpate the fields in the json
3. use the script tool to perform a post of the update json

You can't do a put of fields, you always need to get the full object via the script tool and then update the fields in there and send it back

## Example Interactions

**User**: "Show me all events"
→ Execute GET request, display results as a table.

**User**: "Create a new pro event called 'Summer Gala' on July 15th from 6pm to 11pm"
→ Build the event object, confirm details, then POST.

**User**: "Change the status of 'Summer Gala' to Done"
→ GET events filtered by name, find the Identifier, then PUT with updated Status.

**User**: "Delete the event called 'Test Event'"
→ GET events filtered by name, confirm with user, then DELETE using the Identifier.

**User**: "Show me all events that are still in Processing status"
→ GET with OData filter `Status eq 'Processing'`.