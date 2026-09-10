# DevDesk UI / UX

Approved reference images live in `assets/references/`.

## Identity
- dark Windows developer tool
- professional, not gaming-style
- clear hierarchy
- restrained blue/teal accent
- green success/running
- red destructive/conflict
- amber warning
- readable tables
- useful cards only
- consistent spacing/typography

## MVP navigation
- Dashboard
- Projects
- Processes
- Ports
- Commands
- Settings

Project-specific Logs and Git should primarily live under Project Details.

## Project Details
- Overview
- Logs
- Git
- Commands

## Interaction rules
- destructive actions visually distinct and confirmed
- errors explain reason + next action
- loading must not freeze the UI
- empty states tell the user what to do
- status must not rely only on color
- avoid excessive animation

## Translating mockups to WPF
Preserve hierarchy, spacing rhythm, and visual priority. Implement reusable resources/styles instead of hard-coding every control.
