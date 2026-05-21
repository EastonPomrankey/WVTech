Feature: WVT-183 Ingredient recognition and matching

  As a user
  I want ingredients to be recognized and matched correctly regardless of how they are entered
  So that my meal plans and shopping lists are accurate and free of duplicate or mismatched items

  Background:
    Given there is a user named 'Alice'
    And 'Alice' is logged into Onebite

  Scenario: Ingredient names with different capitalizations are merged on the shopping list
    Given 'Alice' has a meal with ingredient 'Chicken Breast' in 'Cup' with amount '2' for today
    And 'Alice' has a meal with ingredient 'chicken breast' in 'Cup' with amount '3' for today
    When 'Alice' syncs her shopping list for today
    Then the shopping list shows 'chicken breast' once with amount '5'

  Scenario: Unit full name is accepted when adding an item to the shopping list
    Given 'Alice' navigates to the shopping list
    When 'Alice' adds a shopping list item with amount '2' unit 'Teaspoon' and name 'wvt183salt'
    Then 'wvt183salt' appears on the shopping list

  Scenario: Submitting the shopping list add form without a unit shows a validation error
    Given 'Alice' navigates to the shopping list
    When 'Alice' submits the shopping list add form with no unit selected for name 'wvt183flour'
    Then a shopping list error message is shown

  Scenario: Ingredients with convertible units are merged on the shopping list
    Given 'Alice' has a meal with ingredient 'wvt183sugar' in 'Teaspoon' with amount '3' for today
    And 'Alice' has a meal with ingredient 'wvt183sugar' in 'Tablespoon' with amount '1' for today
    When 'Alice' syncs her shopping list for today
    Then the shopping list shows 'wvt183sugar' once with amount '6' in 'Teaspoon'

  Scenario: Adding the same ingredient with the same unit merges the quantities
    Given 'Alice' has 'wvt183butter' with amount '1' and measurement 'Cup' on the shopping list
    When 'Alice' navigates to the shopping list
    And 'Alice' adds a shopping list item with amount '2' unit 'Cup' and name 'wvt183butter'
    Then 'wvt183butter' appears on the shopping list with amount '3'
