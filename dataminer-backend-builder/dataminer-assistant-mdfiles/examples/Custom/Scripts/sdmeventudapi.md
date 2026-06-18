---
name: sdmeventudapi
description: Script to execute create, update and delete actions on an event.
scriptName: sdmeventudapi
inputArguments:
- name: ApiTriggerInput
  description: The ApiTriggerInput input parameter is the serialized variant of the APITriggerInput class. In the background this mimics doing a http request to a webapi
  example: '{"RequestMethod":2,"Route":"eventmanager","RawBody":"{\\"Name\\":\\"test\\",\\"Description\\":\\"fsdfsdf\\",\\"Start\\":\\"2026-05-17T08:56:00.000Z\\",\\"End\\":\\"2026-05-16T08:57:00.000Z\\",\\"Type\\":\\"Basic\\",\\"Status\\":\\"Requested\\",\\"Languages\\":[{\\"Name\\":\\"test\\",\\"AudioType\\":\\"Stereo\\",\\"CcSupplierCompanyName\\":\\"belgie\\"}],\\"Identifier\\":\\"da9166f6-ccbe-4c8d-b991-4528c999eb03\\"}","Parameters":{},"Context":{"TokenId":"00000000-0000-0000-0000-000000000000"},"QueryParameters":{}}'
sync: true
requiresUserValidation: false
---

## ApiTriggerInput Explanation
The ApiTriggerInput input parameter is the serialized variant of the APITriggerInput class. In the background this mimics doing a http request to a webapi

RequestMethod : Enum
  1 - GET
  2 - PUT
  3 - POST
  4 - DELETE

Route : the route of the model you want to get (e.g eventmanager)
RawBody : the http body to SDMEventUDAPI
QueryParameters : A key value dictionary listing query parameters in the http call

## Event Model

Each event has these fields:

| Field | Type | Description |
|-------|------|-------------|
| Name | string | Event name |
| Description | string | Event description |
| Start | datetime (ISO 8601) | Start time |
| End | datetime (ISO 8601) | End time |
| Type | enum | `Basic`, `Pro`, or `Advanced` |
| Status | enum | `Requested`, `Processing`, or `Done` |
| Languages | array | List of language objects (Name, AudioType, CcSupplierCompanyName) |
| Identifier | string | Unique identifier (GUID) |

**AudioType** values: `Stereo`, `Mono`, `Surround`