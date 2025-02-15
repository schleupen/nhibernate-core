using System;
using System.Collections;
using System.Collections.Generic;
using log4net;
using NHibernate.Cache;
using NHibernate.Cache.Entry;
using NHibernate.Criterion;
using NHibernate.Transform;
using NUnit.Framework;

namespace NHibernate.Test.FilterTest
{
	[TestFixture]
	public class DynamicFilterTest : TestCase
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(DynamicFilterTest));
		private TestData testData;

		protected override string CacheConcurrencyStrategy => "nonstrict-read-write";

		protected override void OnSetUp()
		{
			testData = new TestData(this);
			testData.Prepare();
		}

		protected override void OnTearDown()
		{
			testData.Release();
		}

		[Test]
		public void SecondLevelCachedCollectionsFiltering()
		{
			var persister = Sfi
				.GetCollectionPersister(typeof(Salesperson).FullName + ".Orders");
			var cacheKey =
				new CacheKey(testData.steveId, persister.KeyType, persister.Role, Sfi, null);
			CollectionCacheEntry cachedData;

			using (var session = OpenSession())
			{
				// Force a collection into the second level cache, with its non-filtered elements
				var sp = (Salesperson) session.Load(typeof(Salesperson), testData.steveId);
				NHibernateUtil.Initialize(sp.Orders);
				Assert.IsTrue(persister.HasCache, "No cache for collection");
				cachedData = (CollectionCacheEntry) persister.Cache.Cache.Get(cacheKey);
				Assert.IsNotNull(cachedData, "collection was not in cache");
			}

			using (var session = OpenSession())
			{
				session.EnableFilter("fulfilledOrders").SetParameter("asOfDate", testData.lastMonth);
				var sp = (Salesperson) session.CreateQuery("from Salesperson as s where s.id = :id")
				                          .SetInt64("id", testData.steveId)
				                          .UniqueResult();
				Assert.AreEqual(1, sp.Orders.Count, "Filtered-collection not bypassing 2L-cache");

				CollectionCacheEntry cachedData2 = (CollectionCacheEntry) persister.Cache.Cache.Get(cacheKey);
				Assert.IsNotNull(cachedData2, "collection no longer in cache!");
				Assert.AreSame(cachedData, cachedData2, "Different cache values!");
			}

			using (var session = OpenSession())
			{
				session.EnableFilter("fulfilledOrders").SetParameter("asOfDate", testData.lastMonth);
				var sp = (Salesperson) session.Load(typeof(Salesperson), testData.steveId);
				Assert.AreEqual(1, sp.Orders.Count, "Filtered-collection not bypassing 2L-cache");
			}

			// Finally, make sure that the original cached version did not get over-written
			using (var session = OpenSession())
			{
				var sp = (Salesperson) session.Load(typeof(Salesperson), testData.steveId);
				Assert.AreEqual(2, sp.Orders.Count, "Actual cached version got over-written");
			}
		}

		[Test]
		public void CombinedClassAndCollectionFiltersEnabled()
		{
			using (var session = OpenSession())
			{
				session.EnableFilter("regionlist").SetParameterList("regions", new[] { "LA", "APAC" });
				session.EnableFilter("fulfilledOrders").SetParameter("asOfDate", testData.lastMonth);

				// test retreival through hql with the collection as non-eager
				IList salespersons = session.CreateQuery("select s from Salesperson as s").List();
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");
				Salesperson sp = (Salesperson) salespersons[0];
				Assert.AreEqual(1, sp.Orders.Count, "Incorrect order count");

				session.Clear();

				// test retreival through hql with the collection join fetched
				salespersons = session.CreateQuery("select s from Salesperson as s left join fetch s.Orders").List();
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");
				sp = (Salesperson) salespersons[0];
				Assert.AreEqual(sp.Orders.Count, 1, "Incorrect order count");
			}
		}

		[Test]
		public void FiltersWithQueryCache()
		{
			using (var session = OpenSession())
			{
				session.EnableFilter("regionlist").SetParameterList("regions", new[] { "LA", "APAC" });
				session.EnableFilter("fulfilledOrders").SetParameter("asOfDate", testData.lastMonth);

				// test retreival through hql with the collection as non-eager
				IList salespersons = session.CreateQuery("select s from Salesperson as s").SetCacheable(true).List();
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");

				// Try a second time, to make use of query cache
				salespersons = session.CreateQuery("select s from Salesperson as s").SetCacheable(true).List();
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");

				session.Clear();

				// test retreival through hql with the collection join fetched
				salespersons =
					session.CreateQuery("select s from Salesperson as s left join fetch s.Orders").SetCacheable(true).List();
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");

				// A second time, to make use of query cache
				salespersons =
					session.CreateQuery("select s from Salesperson as s left join fetch s.Orders").SetCacheable(true).List();
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");
			}
		}

		[Test]
		public void HqlFilters()
		{
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			// HQL test
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			log.Info("Starting HQL filter tests");
			using (var session = OpenSession())
			{
				session.EnableFilter("region").SetParameter("region", "APAC");

				session.EnableFilter("effectiveDate")
				       .SetParameter("asOfDate", testData.lastMonth);

				log.Info("HQL against Salesperson...");
				IList results = session.CreateQuery("select s from Salesperson as s left join fetch s.Orders").List();
				Assert.IsTrue(results.Count == 1, "Incorrect filtered HQL result count [" + results.Count + "]");
				Salesperson result = (Salesperson) results[0];
				Assert.IsTrue(result.Orders.Count == 1, "Incorrect collectionfilter count");

				log.Info("HQL against Product...");
				results = session.CreateQuery("from Product as p where p.StockNumber = ?").SetInt32(0, 124).List();
				Assert.IsTrue(results.Count == 1);
			}
		}

		[Test]
		public void CriteriaQueryFilters()
		{
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			// Criteria-query test
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			log.Info("Starting Criteria-query filter tests");

			using (var session = OpenSession())
			{
				session.EnableFilter("region").SetParameter("region", "APAC");

				session.EnableFilter("fulfilledOrders")
				       .SetParameter("asOfDate", testData.lastMonth);

				session.EnableFilter("effectiveDate")
				       .SetParameter("asOfDate", testData.lastMonth);

				log.Info("Criteria query against Salesperson...");
				IList salespersons = session.CreateCriteria(typeof(Salesperson))
				                            .Fetch("orders")
				                            .List();
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");
				Assert.AreEqual(1, ((Salesperson) salespersons[0]).Orders.Count, "Incorrect order count");

				log.Info("Criteria query against Product...");
				IList products = session.CreateCriteria(typeof(Product))
				                        .Add(Expression.Eq("StockNumber", 124))
				                        .List();
				Assert.AreEqual(1, products.Count, "Incorrect product count");
			}
		}

		[Test]
		public void CriteriaControl()
		{
			// the subquery...
			var subquery = DetachedCriteria
				.For<Salesperson>()
				.SetProjection(Property.ForName("Name"));

			using (var session = OpenSession())
			using (var transaction = session.BeginTransaction())
			{
				session.EnableFilter("fulfilledOrders").SetParameter("asOfDate", testData.lastMonth.Date);
				session.EnableFilter("regionlist").SetParameter("regions", "APAC");

				var result = session
					.CreateCriteria<Order>()
					.Add(Subqueries.In("steve", subquery))
					.List();

				Assert.That(result.Count, Is.EqualTo(1));

				transaction.Commit();
			}
		}

		[Test]
		public void CriteriaSubqueryWithFilters()
		{
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			// Criteria-subquery test
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			log.Info("Starting Criteria-subquery filter tests");

			using (var session = OpenSession())
			{
				session.EnableFilter("region").SetParameter("region", "APAC");

				log.Info("Criteria query against Department with a subquery on Salesperson in the APAC reqion...");
				var salespersonSubquery = DetachedCriteria.For<Salesperson>()
					.Add(Restrictions.Eq("Name", "steve"))
					.SetProjection(Property.ForName("Department"));

				var departmentsQuery = session.CreateCriteria<Department>().
					Add(Subqueries.PropertyIn("Id", salespersonSubquery));
				var departments = departmentsQuery.List<Department>();

				Assert.That(departments.Count, Is.EqualTo(1), "Incorrect department count");

				log.Info("Criteria query against Department with a subquery on Salesperson in the FooBar reqion...");

				session.EnableFilter("region").SetParameter("region", "Foobar");
				departments = departmentsQuery.List<Department>();

				Assert.That(departments.Count, Is.EqualTo(0), "Incorrect department count");

				log.Info("Criteria query against Order with a subquery for line items with a subquery on product and sold by a given sales person...");
				session.EnableFilter("region").SetParameter("region", "APAC");

				var lineItemSubquery = DetachedCriteria.For<LineItem>()
					.Add(Restrictions.Ge("Quantity", 1L))
					.CreateCriteria("Product")
					.Add(Restrictions.Eq("Name", "Acme Hair Gel"))
					.SetProjection(Property.ForName("Id"));

				var orders = session.CreateCriteria<Order>()
					.Add(Subqueries.Exists(lineItemSubquery))
					.Add(Restrictions.Eq("Buyer", "gavin"))
					.List<Order>();

				Assert.That(orders.Count, Is.EqualTo(1), "Incorrect orders count");

				log.Info("query against Order with a subquery for line items with a subquery line items where the product name is Acme Hair Gel and the quantity is greater than 1 in a given region and the product is effective as of last month");
				session.EnableFilter("region").SetParameter("region", "APAC");
				session.EnableFilter("effectiveDate").SetParameter("asOfDate", testData.lastMonth.Date);

				var productSubquery = DetachedCriteria.For<Product>()
					.
					Add(Restrictions.Eq("Name", "Acme Hair Gel"))
					.SetProjection(Property.ForName("id"));

				lineItemSubquery = DetachedCriteria.For<LineItem>()
					.Add(Restrictions.Ge("Quantity", 1L))
					.CreateCriteria("Product")
					.Add(Subqueries.PropertyIn("Id", productSubquery))
					.SetProjection(Property.ForName("Id"));

				orders = session
					.CreateCriteria<Order>()
					.Add(Subqueries.Exists(lineItemSubquery))
					.Add(Restrictions.Eq("Buyer", "gavin"))
					.List<Order>();

				Assert.That(orders.Count, Is.EqualTo(1), "Incorrect orders count");

				log.Info("query against Order with a subquery for line items with a subquery line items where the product name is Acme Hair Gel and the quantity is greater than 1 in a given region and the product is effective as of 4 months ago");
				session.EnableFilter("region").SetParameter("region", "APAC");
				session.EnableFilter("effectiveDate").SetParameter("asOfDate", testData.fourMonthsAgo.Date);

				orders = session.CreateCriteria<Order>()
					.Add(Subqueries.Exists(lineItemSubquery))
					.Add(Restrictions.Eq("Buyer", "gavin"))
					.List<Order>();

				Assert.That(orders.Count, Is.EqualTo(0), "Incorrect orders count");

				session.Close();
			}
		}

		[Test]
		public void GetFilters()
		{
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			// Get() test
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			log.Info("Starting get() filter tests (eager assoc. fetching).");
			using (var session = OpenSession())
			{
				session.EnableFilter("region").SetParameter("region", "APAC");

				log.Info("Performing get()...");
				Salesperson salesperson = (Salesperson) session.Get(typeof(Salesperson), testData.steveId);
				Assert.IsNotNull(salesperson);
				Assert.AreEqual(1, salesperson.Orders.Count, "Incorrect order count");
			}
		}

		[Test]
		public void OneToManyFilters()
		{
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			// one-to-many loading tests
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			log.Info("Starting one-to-many collection loader filter tests.");
			using (var session = OpenSession())
			{
				session.EnableFilter("seniorSalespersons")
				       .SetParameter("asOfDate", testData.lastMonth);

				log.Info("Performing Load of Department...");
				Department department = (Department) session.Load(typeof(Department), testData.deptId);
				ISet<Salesperson> salespersons = department.Salespersons;
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");
			}
		}

		[Test]
		public void InStyleFilterParameter()
		{
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			// one-to-many loading tests
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			log.Info("Starting one-to-many collection loader filter tests.");
			using (var session = OpenSession())
			{
				session.EnableFilter("regionlist")
				       .SetParameterList("regions", new [] { "LA", "APAC" });

				log.Debug("Performing query of Salespersons");
				IList salespersons = session.CreateQuery("from Salesperson").List();
				Assert.AreEqual(1, salespersons.Count, "Incorrect salesperson count");
			}
		}

		[Test]
		public void InStyleFilterParameterWithHashSet()
		{
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			// one-to-many loading tests
			//~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
			log.Info("Starting one-to-many collection loader filter tests with HashSet.");
			using (var session = OpenSession())
			{
				Assert.Multiple(
					() =>
					{
						session.EnableFilter("regionlist")
						       .SetParameterList("regions", new HashSet<string> { "LA", "APAC" });

						log.Debug("Performing query of Salespersons");
						var salespersons = session.CreateQuery("from Salesperson").List();
						Assert.That(salespersons.Count, Is.EqualTo(1), "Incorrect salesperson count");

						session.EnableFilter("guidlist")
						       .SetParameterList("guids", new HashSet<Guid> { testData.Product1Guid, testData.Product2Guid });

						log.Debug("Performing query of Products");
						var products = session.CreateQuery("from Product").List();
						Assert.That(products.Count, Is.EqualTo(2), "Incorrect product count");
					});
			}
		}

		[Test]
		public void ManyToManyFilterOnCriteria()
		{
			using (var session = OpenSession())
			{
				session.EnableFilter("effectiveDate").SetParameter("asOfDate", DateTime.Today);

				Product prod = (Product) session.CreateCriteria(typeof(Product))
				                                .SetResultTransformer(new DistinctRootEntityResultTransformer())
				                                .Add(Expression.Eq("id", testData.prod1Id))
				                                .UniqueResult();

				Assert.IsNotNull(prod);
				Assert.AreEqual(1, prod.Categories.Count, "Incorrect Product.categories count for filter");
			}
		}

		[Test]
		public void ManyToManyFilterOnLoad()
		{
			using (var session = OpenSession())
			{
				session.EnableFilter("effectiveDate").SetParameter("asOfDate", DateTime.Today);

				Product prod = (Product) session.Get(typeof(Product), testData.prod1Id);

				//long initLoadCount = sessions.Statistics.CollectionLoadCount;
				//long initFetchCount = sessions.Statistics.CollectionFetchCount;

				// should already have been initialized...
				Assert.IsTrue(NHibernateUtil.IsInitialized(prod.Categories));
				int size = prod.Categories.Count;
				Assert.AreEqual(1, size, "Incorrect filtered collection count");

				//long currLoadCount = sessions.Statistics.CollectionLoadCount;
				//long currFetchCount = sessions.Statistics.CollectionFetchCount;

				//Assert.IsTrue(
				//    (initLoadCount == currLoadCount) && (initFetchCount == currFetchCount),
				//    "Load with join fetch of many-to-many did not trigger join fetch"
				//    );

				// make sure we did not get back a collection of proxies
				//long initEntityLoadCount = sessions.Statistics.EntityLoadCount;

				foreach (Category cat in prod.Categories)
				{
					Assert.IsTrue(
						NHibernateUtil.IsInitialized(cat),
						"Load with join fetch of many-to-many did not trigger *complete* join fetch");
					//Console.WriteLine(" ===> " + cat.Name);
				}
				//long currEntityLoadCount = sessions.Statistics.EntityLoadCount;

				//Assert.IsTrue(
				//    (initEntityLoadCount == currEntityLoadCount),
				//    "Load with join fetch of many-to-many did not trigger *complete* join fetch"
				//    );
			}
		}

		[Test]
		public void ManyToManyOnCollectionLoadAfterHQL()
		{
			using (var session = OpenSession())
			{
				session.EnableFilter("effectiveDate").SetParameter("asOfDate", DateTime.Today);

				// Force the categories to not get initialized here
				IList result = session.CreateQuery("from Product as p where p.id = :id")
				                      .SetInt64("id", testData.prod1Id)
				                      .List();
				Assert.IsTrue(result.Count > 0, "No products returned from HQL");

				Product prod = (Product) result[0];
				Assert.IsNotNull(prod);
				Assert.AreEqual(1, prod.Categories.Count, "Incorrect Product.categories count for filter on collection Load");
			}
		}

		[Test]
		public void ManyToManyFilterOnQuery()
		{
			using (var session = OpenSession())
			{
				session.EnableFilter("effectiveDate").SetParameter("asOfDate", DateTime.Today);

				IList result = session.CreateQuery("from Product p inner join fetch p.Categories").List();
				Assert.IsTrue(result.Count > 0, "No products returned from HQL many-to-many filter case");

				Product prod = (Product) result[0];

				Assert.IsNotNull(prod);
				Assert.AreEqual(1, prod.Categories.Count, "Incorrect Product.categories count for filter with HQL");
			}
		}

		[Test]
		public void ManyToManyBase()
		{
			using (var session = OpenSession())
			{
				Product prod = (Product) session.Get(typeof(Product), testData.prod1Id);

				// TODO H3: Statistics
				//long initLoadCount = sessions.Statistics.CollectionLoadCount;
				//long initFetchCount = sessions.Statistics.CollectionFetchCount;

				// should already have been initialized...
				Assert.IsTrue(
					NHibernateUtil.IsInitialized(prod.Categories),
					"Load with join fetch of many-to-many did not trigger join fetch");
				int size = prod.Categories.Count;
				Assert.AreEqual(2, size, "Incorrect non-filtered collection count");

				//long currLoadCount = sessions.Statistics.CollectionLoadCount;
				//long currFetchCount = sessions.Statistics.CollectionFetchCount;

				//Assert.IsTrue(
				//        ( initLoadCount == currLoadCount ) && ( initFetchCount == currFetchCount ),
				//        "Load with join fetch of many-to-many did not trigger join fetch"
				//);

				// make sure we did not get back a collection of proxies
				// TODO H3: statistics
				//long initEntityLoadCount = sessions.Statistics.EntityLoadCount;
				foreach (Category cat in prod.Categories)
				{
					Assert.IsTrue(
						NHibernateUtil.IsInitialized(cat),
						"Load with join fetch of many-to-many did not trigger *complete* join fetch");
					//Console.WriteLine(" ===> " + cat.Name);
				}
				//long currEntityLoadCount = sessions.Statistics.EntityLoadCount;

				//Assert.IsTrue(
				//        ( initEntityLoadCount == currEntityLoadCount ),
				//        "Load with join fetch of many-to-many did not trigger *complete* join fetch"
				//);
			}
		}

		[Test]
		public void ManyToManyBaseThruCriteria()
		{
			using (var session = OpenSession())
			{
				IList result = session.CreateCriteria(typeof(Product))
				                      .Add(Expression.Eq("id", testData.prod1Id))
				                      .List();

				Product prod = (Product) result[0];

				//long initLoadCount = sessions.Statistics.CollectionLoadCount;
				//long initFetchCount = sessions.Statistics.CollectionFetchCount;

				// should already have been initialized...
				Assert.IsTrue(
					NHibernateUtil.IsInitialized(prod.Categories),
					"Load with join fetch of many-to-many did not trigger join fetch");
				int size = prod.Categories.Count;
				Assert.AreEqual(2, size, "Incorrect non-filtered collection count");

				//long currLoadCount = sessions.Statistics.CollectionLoadCount;
				//long currFetchCount = sessions.Statistics.CollectionFetchCount;

				//Assert.IsTrue(
				//    (initLoadCount == currLoadCount) && (initFetchCount == currFetchCount),
				//    "Load with join fetch of many-to-many did not trigger join fetch"
				//    );

				// make sure we did not get back a collection of proxies
				//long initEntityLoadCount = sessions.Statistics.EntityLoadCount;
				foreach (Category cat in prod.Categories)
				{
					Assert.IsTrue(
						NHibernateUtil.IsInitialized(cat),
						"Load with join fetch of many-to-many did not trigger *complete* join fetch");
					//Console.WriteLine(" ===> " + cat.Name);
				}
				//long currEntityLoadCount = sessions.Statistics.EntityLoadCount;

				//Assert.IsTrue(
				//    (initEntityLoadCount == currEntityLoadCount),
				//    "Load with join fetch of many-to-many did not trigger *complete* join fetch"
				//    );
			}
		}

		protected override string MappingsAssembly => "NHibernate.Test";

		protected override string[] Mappings => new []
		{
			"FilterTest.defs.hbm.xml",
			"FilterTest.classes.hbm.xml",
		};

		private class TestData
		{
			public long steveId;
			public long deptId;
			public long prod1Id;
			public DateTime lastMonth;
			public DateTime nextMonth;
			public DateTime sixMonthsAgo;
			public DateTime fourMonthsAgo;
			public Guid Product1Guid;
			public Guid Product2Guid;

			private DynamicFilterTest outer;

			public TestData(DynamicFilterTest outer)
			{
				this.outer = outer;
			}

			private readonly IList<object> entitiesToCleanUp = new List<object>();

			public void Prepare()
			{
				using (var session = outer.OpenSession())
				using (var transaction = session.BeginTransaction())
				{
					lastMonth = DateTime.Today.AddMonths(-1);
					nextMonth = DateTime.Today.AddMonths(1);

					sixMonthsAgo = DateTime.Today.AddMonths(-6);
					fourMonthsAgo = DateTime.Today.AddMonths(-4);

					Department dept = new Department();
					dept.Name = ("Sales");

					session.Save(dept);
					deptId = dept.Id;
					entitiesToCleanUp.Add(dept);

					Salesperson steve = new Salesperson();
					steve.Name = ("steve");
					steve.Region = ("APAC");
					steve.HireDate = (sixMonthsAgo);

					steve.Department = (dept);
					dept.Salespersons.Add(steve);

					Salesperson max = new Salesperson();
					max.Name = ("max");
					max.Region = ("EMEA");
					max.HireDate = (nextMonth);

					max.Department = (dept);
					dept.Salespersons.Add(max);

					session.Save(steve);
					session.Save(max);
					entitiesToCleanUp.Add(steve);
					entitiesToCleanUp.Add(max);

					steveId = steve.Id;

					Category cat1 = new Category("test cat 1", lastMonth, nextMonth);
					Category cat2 = new Category("test cat 2", sixMonthsAgo, fourMonthsAgo);

					Product product1 = new Product();
					product1.Name = ("Acme Hair Gel");
					product1.StockNumber = (123);
					product1.EffectiveStartDate = (lastMonth);
					product1.EffectiveEndDate = (nextMonth);
					product1.ProductGuid = Guid.NewGuid();
					Product1Guid = product1.ProductGuid;

					product1.AddCategory(cat1);
					product1.AddCategory(cat2);

					session.Save(product1);
					entitiesToCleanUp.Add(product1);
					prod1Id = product1.Id;

					Order order1 = new Order();
					order1.Buyer = "gavin";
					order1.Region = ("APAC");
					order1.PlacementDate = sixMonthsAgo;
					order1.FulfillmentDate = fourMonthsAgo;
					order1.Salesperson = steve;
					order1.AddLineItem(product1, 500);

					session.Save(order1);
					entitiesToCleanUp.Add(order1);

					Product product2 = new Product();
					product2.Name = ("Acme Super-Duper DTO Factory");
					product2.StockNumber = (124);
					product2.EffectiveStartDate = (sixMonthsAgo);
					product2.EffectiveEndDate = (DateTime.Today);
					product2.ProductGuid = Guid.NewGuid();
					Product2Guid = product2.ProductGuid;

					Category cat3 = new Category("test cat 2", sixMonthsAgo, DateTime.Today);
					product2.AddCategory(cat3);

					session.Save(product2);
					entitiesToCleanUp.Add(product2);

					// An uncategorized product
					Product product3 = new Product();
					product3.Name = ("Uncategorized product");
					session.Save(product3);
					entitiesToCleanUp.Add(product3);

					Order order2 = new Order();
					order2.Buyer = "christian";
					order2.Region = ("EMEA");
					order2.PlacementDate = lastMonth;
					order2.Salesperson = steve;
					order2.AddLineItem(product2, -1);

					session.Save(order2);
					entitiesToCleanUp.Add(order2);

					transaction.Commit();
				}
			}

			public void Release()
			{
				using (var session = outer.OpenSession())
				using (var transaction = session.BeginTransaction())
				{
					foreach (var obj in entitiesToCleanUp)
					{
						session.Delete(obj);
					}

					transaction.Commit();
				}
			}
		}
	}
}
