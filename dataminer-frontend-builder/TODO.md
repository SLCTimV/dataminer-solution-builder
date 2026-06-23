# TODO — DataMiner Frontend Builder

## Styling must come exclusively from the DataMiner App Builder agent

The frontend builder (`New-UiBuilder.cs` / `New-UiInstaller.cs`) must **not** define or inject any CSS styles itself. All styling must originate from:
- The `DataMiner App Builder` agent's own skills and style definitions
- The `dataminer-frontend` skill (design tokens, color palette, component classes)

### Rules

- [ ] `New-UiBuilder.cs` must not contain any inline CSS, embedded stylesheets, or hardcoded style values
- [ ] The frontend builder delegates fully to the App Builder agent — styling decisions live in that agent's instructions, not in the scaffolding script
- [ ] If the generated output has inconsistent styles, fix the App Builder agent's skills/prompt — never patch styles in the frontend builder layer
- [ ] The `dataminer-frontend-builder` SKILL.md should document this boundary: "styling is owned by the App Builder agent"

## Verify preferred login method in the DataMiner App Builder agent

- [ ] Check what login/auth method the App Builder agent uses by default (SAML redirect, username/password form, or other)
- [ ] Ensure the frontend builder's generated auth flow matches the App Builder's preferred login method
- [ ] If there's a mismatch, update the App Builder agent's instructions to use SAML redirect as the default

## Filter section: replace plain textbox with an OData query composer overlay

- [ ] The generated filter UI should not be a simple text input for raw OData strings
- [ ] Instead, provide an overlay/popover that lets users compose an OData filter visually (select field, operator, value) and builds the `$filter` string from the selections
- [ ] The overlay should support common operators (eq, ne, gt, lt, contains, startswith) and allow combining multiple conditions (and/or)
- [ ] The raw OData string should still be visible (read-only or editable as an advanced option) so power users can verify/tweak it
