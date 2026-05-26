Feature: WVT-169 External recipe source redirect

  As a user
  I want to be redirected to the original recipe source when viewing an external recipe
  So that I can access the full instructions without the app needing to store or replicate recipe content

  Background:
    Given there is a user named 'Alice'
    And 'Alice' is logged into Onebite

  Scenario: Viewing a local recipe navigates to the in-app detail page
    Given a local recipe named 'wvt169LocalPasta' exists in the database
    When 'Alice' navigates to the detail page for 'wvt169LocalPasta'
    Then the in-app recipe detail page is shown for 'wvt169LocalPasta'

  Scenario: Navigating directly to an external recipe shows the redirect page
    Given an external recipe named 'wvt169ExternalTacos' with source URL 'https://www.example.com/wvt169-tacos' exists in the database
    When 'Alice' navigates to the detail page for 'wvt169ExternalTacos'
    Then the external redirect page is shown with a link to 'https://www.example.com/wvt169-tacos'

  Scenario: An external recipe with an invalid source URL shows a friendly error
    Given an external recipe named 'wvt169BrokenRecipe' with source URL 'not-a-valid-url' exists in the database
    When 'Alice' navigates to the detail page for 'wvt169BrokenRecipe'
    Then 'Alice' is redirected to the recipe search page

  Scenario: An Edamam recipe detail page shows a link to the original source when available
    Given a local recipe named 'wvt169EdamamRecipe' exists in the database with an Edamam URI
    When 'Alice' navigates to the detail page for 'wvt169EdamamRecipe'
    Then the in-app recipe detail page is shown for 'wvt169EdamamRecipe'
    And a view full recipe link is present on the page
