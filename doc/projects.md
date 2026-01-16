# Project Ideas
## Meal Planner & Recipe Finder
This project idea is a meal planner combined with a recipe finder. This will be built using ASP.NET Core MVC. The purpose is that users will be able to create an account using an email, search and save different recipes as well as create their own, and then plan their meals on a calendar. Other features can include downloading recipes as a pdf document if one wants to print out the recipe. External APIs such as TheMealDB or Spoonacular will be used to find recipes, ingredients, and nutritional facts. The user, when planning their meals, can assign to specific days, are able to repeat meals, and are able to create a grocery list based on meals if they would like. An idea for the algorithmic component would be meal recommendations on top of finding recipes on their own. And with the recommendations, the user can set up restrictions – diet type, macro tracker/goals, budget, etc. 

## Flipd
### Product Overview
Flipd is an employee search site that allows employers to look for highly qualified employees to fill positions. People looking for a job can add their resume, image, skills, etc. Employees will also have a post page that allows them to let others know what experiences they’ve had with employers and to ask and give advice. 
The purpose of this site is to put hiring power back into the employee. Instead of having the employee search endlessly for jobs and receive dozens of rejection letters, why not have employers look for the people they need? 
### External API
- **Auth0** for identity authentication of employers and employees.
### Algorithmic Component
- Allows employers to search based on the skills they want people to have for a position.

## Product development dashboard
A website for users who are not themselves data analyists to easily create dashboards for the development of products and aggregate information and opinions about the product domain to provide new insights. An example target user would be a game developer, who would want to gain insight into what game genres are popular. Or a bike shop, who would like to know what mountain bike parts are being shared around on social media.
### Algorithmic Component
The charts and statistics the user would most likely need for their product domain could be populated with a recommendation algorithm. Additionally, the sentiment analysis may also be achieved through the use of an deep learning algorithm, likely using a pretrained LLM, such as DeepSeek, as a base for training.

### Potential APIs: 
- Finnhub Stock API
- Bluesky
- Graph API (Facebook)
- Steam Web API
- Overpass API (OpenStreetMaps)
- Associated Press? Maybe if we ask nicely