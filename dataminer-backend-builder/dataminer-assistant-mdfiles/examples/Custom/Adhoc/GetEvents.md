---
name: GetEvents
description: 'Is able to list all events  '
dataSource: '{"DataSourceInterface":"Skyline.DataMiner.Analytics.GenericInterface.IGQIDataSource","ScriptName":"SDMEventGQI","LibraryName":"SDMEventGQI","TypeFullName":"SDMEventGQI.Events.GetEvents","TypeName":"GetEvents"}'
columns:
- name: Identifier
  type: string
  description: ''
- name: Name
  type: string
  description: ''
- name: Description
  type: string
  description: ''
- name: Start
  type: date
  description: ''
- name: End
  type: date
  description: ''
- name: Type
  type: number
  description: ''
- name: Status
  type: number
  description: ''
inputArguments:
- name: FilterRequest
  type: string
  description: ''
  example: ''
---

gets all events