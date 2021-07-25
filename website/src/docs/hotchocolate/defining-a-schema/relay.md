---
title: "Relay"
---

import { ExampleTabs } from "../../../components/mdx/example-tabs"

TODO

[Learn more about the Relay GraphQL Server Specification](https://relay.dev/docs/guides/graphql-server-specification)

# Global Object Identification

Global Object Identification, as the name suggests, is about being able to uniquely identify an object within our schema. Moreover, it is supposed to allow consumers of our schema to refetch an object in a standardized way, by providing a unique identifier. This capability allows client applications, such as [Relay](https://relay.dev), to automatically refetch types

To identify types that can be refetched, it introduces a new `Node` interface type.

```sdl
interface Node {
  id: ID!
}
```

Implementing this type signals to client applications, that the implementing type can be refetched. Implementing it also enforces the existance of an `id` field, containing a unique identifier, needed for the refetch operation.

<!-- todo: code to automatically open all external links in a new tab: <a target="_blank" rel="noopener noreferrer" href="link">...</a> -->

[Learn more about Global Object Identification](https://graphql.org/learn/global-object-identification)

In Hot Chocolate we can enable Global Object Identification, by calling `EnableRelaySupport()` on the `IRequestExecutorBuilder`.

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddGraphQLServer()
            .EnableRelaySupport()
            .AddQueryType<Query>();
    }
}
```

This registers the `Node` interface type and adds the `node(id: ID!): Node` field to our query type, as explained above.

> ⚠️ Note: Using `EnableRelaySupport()` in two stitched services does currently not work.

<ExampleTabs>
<ExampleTabs.Annotation>

TODO

</ExampleTabs.Annotation>
<ExampleTabs.Code>

```csharp
public class User
{
    public string Id { get; set; }

    public string Name { get; set; }
}

public class UserType : ObjectType<User>
{
    protected override void Configure(IObjectTypeDescriptor<User> descriptor)
    {
        descriptor
            .ImplementsNode()
            .IdField(f => f.Id)
            .ResolveNode(async (context, id) =>
            {
                User user =
                    await context.Service<UserService>().GetByIdAsync(id);

                return user;
            });
    }
}
```

</ExampleTabs.Code>
<ExampleTabs.Schema>

TODO

</ExampleTabs.Schema>
</ExampleTabs>

Since node resolvers resolve entities by their Id, they are the perfect place to start utilizing DataLoaders.

[Learn more about DataLoaders](/docs/hotchocolate/fetching-data/dataloader)

# Connections

TODO

[Learn more about Connections](/docs/hotchocolate/fetching-data/pagination#connections)

# Query field in Mutation payloads

It's a common best practice to return a payload type from mutations containing the affected entity as a field.

```sdl
type Mutation {
  likePost(id: ID!): LikePostPayload
}

type LikePostPayload {
  post: Post
}
```

This allows us to immediately use the affected entity in the client application responsible for the mutation.

Sometimes a mutation might also affect other parts of our application as well. Maybe the `likePost` mutation needs to update an Activity Feed.

For this scenario we can expose a `query` field on our payload type to allow the client application to fetch everything it needs to update its state in one round trip.

```sdl
type LikePostPayload {
  post: Post
  query: Query
}
```

A resulting mutation request could look like the following.

```graphql
mutation {
  likePost(id: 1) {
    post {
      id
      content
      likes
    }
    query {
      ...ActivityFeed_Fragment
    }
  }
}
```

Hot Chocolate allows us to automatically add this `query` field to all of our mutation payload types.

We can enable it like the following:

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddGraphQLServer()
            .EnableRelaySupport(new RelayOptions
            {
                AddQueryFieldToMutationPayloads = true
            });
    }
}
```

By default, this will add a field of type `Query` called `query` to each top-level mutation field type, whose name ends in `Payload`.

Of course these defaults can be tweaked:

```csharp
services
    .AddGraphQLServer()
    .EnableRelaySupport(new RelayOptions
    {
        AddQueryFieldToMutationPayloads = true,
        QueryFieldName = "rootQuery",
        MutationPayloadPredicate = (type) => type.Name.Value.EndsWith("Result")
    });
```

This would add a field of type `Query` with the name of `rootQuery` to each top-level mutation field type, whose name ends in `Result`.
